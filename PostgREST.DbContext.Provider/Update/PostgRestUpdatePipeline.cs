using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Update;

using PosgREST.DbContext.Provider.Core.Infrastructure;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PosgREST.DbContext.Provider.Core.Update;

/// <summary>
/// Translates a list of <see cref="IUpdateEntry"/> items into PostgREST HTTP
/// requests and executes them against the configured PostgREST instance.
/// </summary>
/// <remarks>
/// Each entry maps to exactly one HTTP request:
/// <list type="bullet">
///   <item><see cref="EntityState.Added"/> → <c>POST /{table}</c></item>
///   <item><see cref="EntityState.Modified"/> → <c>PATCH /{table}?{pk}=eq.{value}</c></item>
///   <item><see cref="EntityState.Deleted"/> → <c>DELETE /{table}?{pk}=eq.{value}</c></item>
/// </list>
/// All mutating requests include <c>Prefer: return=representation</c> so PostgREST
/// returns the affected row, allowing store-generated values to be propagated back
/// into the EF Core change tracker.
/// </remarks>
public sealed class PostgRestUpdatePipeline
{
    private readonly HttpClient _httpClient;
    private readonly PostgRestDbContextOptionsExtension _options;

    /// <summary>
    /// Creates a new update pipeline.
    /// </summary>
    public PostgRestUpdatePipeline(
        HttpClient httpClient,
        PostgRestDbContextOptionsExtension options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    /// <summary>
    /// Executes all pending modifications synchronously.
    /// </summary>
    /// <returns>The number of rows affected.</returns>
    public int Execute(IList<IUpdateEntry> entries)
    {
        var ordered = OrderAndFilterEntries(entries);
        var affected = 0;

        foreach (var entry in ordered)
        {
            using var request = BuildRequest(entry);
            using var response = _httpClient.Send(request, HttpCompletionOption.ResponseContentRead);
            PostgRestException.ThrowIfError(response);

            PropagateServerValues(entry, response);
            affected++;
        }

        return affected;
    }

    /// <summary>
    /// Executes all pending modifications asynchronously.
    /// </summary>
    /// <returns>The number of rows affected.</returns>
    public async Task<int> ExecuteAsync(
        IList<IUpdateEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var ordered = OrderAndFilterEntries(entries);
        var affected = 0;

        foreach (var entry in ordered)
        {
            using var request = BuildRequest(entry);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            await PostgRestException.ThrowIfErrorAsync(response, cancellationToken)
                .ConfigureAwait(false);

            await PropagateServerValuesAsync(entry, response, cancellationToken)
                .ConfigureAwait(false);
            affected++;
        }

        return affected;
    }

    // ──────────────────────────────────────────────
    //  Ordering & cascade-delete filtering
    // ──────────────────────────────────────────────

    /// <summary>
    /// Orders entries so that:
    /// <list type="bullet">
    ///   <item>Inserts: parents before children (so FK targets exist).</item>
    ///   <item>Deletes: parents before children (DB cascade handles dependents).</item>
    ///   <item>Modified entries are emitted between inserts and deletes.</item>
    /// </list>
    /// Child entries whose parent is also being deleted and whose FK relationship
    /// uses <see cref="DeleteBehavior.Cascade"/> are removed entirely — the
    /// database will cascade-delete them when the parent row is removed.
    /// </summary>
    private static List<IUpdateEntry> OrderAndFilterEntries(IList<IUpdateEntry> entries)
    {
        var inserts = new List<IUpdateEntry>();
        var updates = new List<IUpdateEntry>();
        var deletes = new List<IUpdateEntry>();

        foreach (var entry in entries)
        {
            switch (entry.EntityState)
            {
                case EntityState.Added:
                    inserts.Add(entry);
                    break;
                case EntityState.Modified:
                    updates.Add(entry);
                    break;
                case EntityState.Deleted:
                    deletes.Add(entry);
                    break;
                // Detached / Unchanged — skip
            }
        }

        // Build a lookup of entity types being deleted so we can detect
        // whether a child's principal is also in the delete set.
        var deletedByType = deletes
            .GroupBy(e => e.EntityType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Filter out child deletes that will be cascade-deleted by the DB.
        var filteredDeletes = new List<IUpdateEntry>(deletes.Count);
        foreach (var entry in deletes)
        {
            if (IsCascadeDeletedByParentInBatch(entry, deletedByType))
                continue;

            filteredDeletes.Add(entry);
        }

        // Topological sort: parents first for both inserts and deletes.
        TopologicalSort(inserts);
        TopologicalSort(filteredDeletes);

        // Final order: inserts (parents→children), updates, deletes (parents→children,
        // the DB cascades children when the parent DELETE arrives).
        var result = new List<IUpdateEntry>(inserts.Count + updates.Count + filteredDeletes.Count);
        result.AddRange(inserts);
        result.AddRange(updates);
        result.AddRange(filteredDeletes);
        return result;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="entry"/> is a dependent (child)
    /// whose principal (parent) is also being deleted in this batch and the
    /// relationship is configured with a server-side cascade delete.
    /// </summary>
    private static bool IsCascadeDeletedByParentInBatch(
        IUpdateEntry entry,
        Dictionary<IEntityType, List<IUpdateEntry>> deletedByType)
    {
        foreach (var fk in entry.EntityType.GetForeignKeys())
        {
            if (fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.ClientCascade))
                continue;

            var principalType = fk.PrincipalEntityType;
            if (!deletedByType.TryGetValue(principalType, out var principalDeletes))
                continue;

            // Check whether the actual principal row this child references is in
            // the delete set (not just any row of that type).
            foreach (var principalEntry in principalDeletes)
            {
                if (ForeignKeyPointsToPrincipal(entry, fk, principalEntry))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="childEntry"/>'s FK property values
    /// match <paramref name="principalEntry"/>'s principal key values.
    /// </summary>
    private static bool ForeignKeyPointsToPrincipal(
        IUpdateEntry childEntry,
        IForeignKey fk,
        IUpdateEntry principalEntry)
    {
        var fkProps = fk.Properties;
        var pkProps = fk.PrincipalKey.Properties;

        for (var i = 0; i < fkProps.Count; i++)
        {
            var childValue = childEntry.GetOriginalValue(fkProps[i]);
            var parentValue = principalEntry.GetOriginalValue(pkProps[i]);

            if (!Equals(childValue, parentValue))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Performs a stable topological sort on <paramref name="entries"/> so that
    /// principals (parents) come before dependents (children).
    /// Uses depth as a sort key: entities with no FK dependencies have depth 0;
    /// entities that depend on another entity in the list have depth = parent + 1.
    /// </summary>
    private static void TopologicalSort(List<IUpdateEntry> entries)
    {
        if (entries.Count <= 1)
            return;

        var entityTypesInBatch = new HashSet<IEntityType>(entries.Select(e => e.EntityType));

        static int GetDepth(IEntityType type, HashSet<IEntityType> inBatch, Dictionary<IEntityType, int> cache)
        {
            if (cache.TryGetValue(type, out var cached))
                return cached;

            // Prevent infinite recursion from self-referencing FKs
            cache[type] = 0;

            var maxParentDepth = -1;
            foreach (var fk in type.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType;
                if (principal == type || !inBatch.Contains(principal))
                    continue;

                var parentDepth = GetDepth(principal, inBatch, cache);
                if (parentDepth > maxParentDepth)
                    maxParentDepth = parentDepth;
            }

            var depth = maxParentDepth + 1;
            cache[type] = depth;
            return depth;
        }

        var depthCache = new Dictionary<IEntityType, int>();
        entries.Sort((a, b) =>
        {
            var da = GetDepth(a.EntityType, entityTypesInBatch, depthCache);
            var db = GetDepth(b.EntityType, entityTypesInBatch, depthCache);
            return da.CompareTo(db);
        });
    }

    // ──────────────────────────────────────────────
    //  Request building
    // ──────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(IUpdateEntry entry)
    {
        return entry.EntityState switch
        {
            EntityState.Added => BuildPostRequest(entry),
            EntityState.Modified => BuildPatchRequest(entry),
            EntityState.Deleted => BuildDeleteRequest(entry),
            _ => throw new InvalidOperationException(
                $"Unexpected EntityState '{entry.EntityState}' for update pipeline.")
        };
    }

    private HttpRequestMessage BuildPostRequest(IUpdateEntry entry)
    {
        var tableName = entry.EntityType.TableName;
        var url = $"{BaseUrl}/{tableName}";
        var body = SerializeAllProperties(entry);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = CreateJsonContent(body)
        };
        ApplyCommonHeaders(request, writeProfile: true);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        return request;
    }

    private HttpRequestMessage BuildPatchRequest(IUpdateEntry entry)
    {
        var tableName = entry.EntityType.TableName;
        var pkFilter = BuildPrimaryKeyFilter(entry);
        var url = $"{BaseUrl}/{tableName}?{pkFilter}";
        var body = SerializeModifiedProperties(entry);

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = CreateJsonContent(body)
        };
        ApplyCommonHeaders(request, writeProfile: true);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        return request;
    }

    private HttpRequestMessage BuildDeleteRequest(IUpdateEntry entry)
    {
        var tableName = entry.EntityType.TableName;
        var pkFilter = BuildPrimaryKeyFilter(entry);
        var url = $"{BaseUrl}/{tableName}?{pkFilter}";

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        ApplyCommonHeaders(request, writeProfile: false);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        return request;
    }

    // ──────────────────────────────────────────────
    //  Serialization
    // ──────────────────────────────────────────────

    private static string SerializeAllProperties(IUpdateEntry entry)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in entry.EntityType.GetProperties())
        {
            // Skip store-generated properties with temporary values (e.g., auto-increment IDs)
            if (property.ValueGenerated != ValueGenerated.Never
                && entry.HasTemporaryValue(property))
                continue;

            var columnName = property.ColumnName;
            var value = entry.GetCurrentValue(property);
            dict[columnName] = FormatPropertyValue(value);
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeModifiedProperties(IUpdateEntry entry)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in entry.EntityType.GetProperties())
        {
            if (!entry.IsModified(property))
                continue;

            var columnName = property.ColumnName;
            var value = entry.GetCurrentValue(property);
            dict[columnName] = FormatPropertyValue(value);
        }

        return JsonSerializer.Serialize(dict);
    }

    private static object? FormatPropertyValue(object? value) => value switch
    {
        null => null,
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF"),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };

    // ──────────────────────────────────────────────
    //  Primary key filter
    // ──────────────────────────────────────────────

    private static string BuildPrimaryKeyFilter(IUpdateEntry entry)
    {
        var pk = entry.EntityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{entry.EntityType.DisplayName()}' has no primary key. " +
                "PostgREST requires a primary key for PATCH and DELETE operations.");

        var segments = new List<string>(pk.Properties.Count);

        foreach (var pkProperty in pk.Properties)
        {
            var columnName = pkProperty.ColumnName;
            // Use original value for the filter — the PK might have been changed
            var value = entry.GetOriginalValue(pkProperty);
            var formatted = FormatFilterValue(value);
            segments.Add($"{columnName}=eq.{formatted}");
        }

        return string.Join("&", segments);
    }

    private static string FormatFilterValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm:ss"),
        _ => value.ToString() ?? "null"
    };

    // ──────────────────────────────────────────────
    //  Response handling — propagate server values
    // ──────────────────────────────────────────────

    private static void PropagateServerValues(IUpdateEntry entry, HttpResponseMessage response)
    {
        if (entry.EntityState == EntityState.Deleted)
            return;

        using var stream = response.Content.ReadAsStream();
        PropagateFromStream(entry, stream);
    }

    private static async Task PropagateServerValuesAsync(
        IUpdateEntry entry,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (entry.EntityState == EntityState.Deleted)
            return;

        using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        PropagateFromStream(entry, stream);
    }

    private static void PropagateFromStream(IUpdateEntry entry, Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);

        // PostgREST returns an array when Prefer: return=representation is used.
        var element = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().FirstOrDefault()
            : doc.RootElement;

        if (element.ValueKind == JsonValueKind.Undefined
            || element.ValueKind == JsonValueKind.Null)
            return;

        foreach (var property in entry.EntityType.GetProperties())
        {
            if (property.ValueGenerated == ValueGenerated.Never)
                continue;

            var columnName = property.ColumnName;
            if (!element.TryGetProperty(columnName, out var jsonProp))
                continue;

            var value = ConvertJsonValue(jsonProp, property.ClrType);
            entry.SetStoreGeneratedValue(property, value, setModified: false);
        }
    }

    private static object? ConvertJsonValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(int)) return element.GetInt32();
        if (underlying == typeof(long)) return element.GetInt64();
        if (underlying == typeof(short)) return element.GetInt16();
        if (underlying == typeof(byte)) return element.GetByte();
        if (underlying == typeof(bool)) return element.GetBoolean();
        if (underlying == typeof(string)) return element.GetString();
        if (underlying == typeof(decimal)) return element.GetDecimal();
        if (underlying == typeof(double)) return element.GetDouble();
        if (underlying == typeof(float)) return element.GetSingle();
        if (underlying == typeof(Guid)) return element.GetGuid();
        if (underlying == typeof(DateTime)) return element.GetDateTime();
        if (underlying == typeof(DateTimeOffset)) return element.GetDateTimeOffset();
        if (underlying == typeof(DateOnly) && element.GetString() is { } dateStr)
            return DateOnly.Parse(dateStr);
        if (underlying == typeof(TimeOnly) && element.GetString() is { } timeStr)
            return TimeOnly.Parse(timeStr);
        if (underlying == typeof(byte[]))
            return element.GetBytesFromBase64();

        return JsonSerializer.Deserialize(element.GetRawText(), targetType);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private string BaseUrl => _options.BaseUrl.TrimEnd('/');

    private static StringContent CreateJsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    private void ApplyCommonHeaders(HttpRequestMessage request, bool writeProfile)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (_options.BearerToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (_options.Schema is { } schema)
        {
            request.Headers.TryAddWithoutValidation("Accept-Profile", schema);
            if (writeProfile)
                request.Headers.TryAddWithoutValidation("Content-Profile", schema);
        }
    }
}
