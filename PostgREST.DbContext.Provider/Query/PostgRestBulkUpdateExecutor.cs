using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Static helper that executes a PostgREST bulk-update (<c>PATCH</c>) request
/// produced by <c>DbSet&lt;T&gt;.ExecuteUpdate / ExecuteUpdateAsync</c>.
/// </summary>
/// <remarks>
/// The method is called via a compiled expression tree emitted by
/// <see cref="PostgRestQueryableMethodTranslatingExpressionVisitor.TranslateExecuteUpdate"/>.
/// After a successful PATCH any entities of the same type that are already tracked
/// by the change tracker are updated in-memory so they reflect the new values,
/// matching the behavior expected by callers who hold a tracked reference.
/// </remarks>
public static class PostgRestBulkUpdateExecutor
{
    /// <summary>
    /// Sends a synchronous <c>PATCH</c> request to PostgREST and returns the number
    /// of rows reported affected by the <c>Content-Range</c> response header.
    /// </summary>
    public static int Execute(
        QueryContext queryContext,
        string tableName,
        IEntityType entityType,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters,
        IReadOnlyList<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)> setters)
    {
        var ctx = (PostgRestQueryContext)queryContext;

        using var request = BuildRequest(ctx, tableName, filters, orFilters, setters);
        using var response = ctx.HttpClient.Send(request, HttpCompletionOption.ResponseContentRead);
        PostgRestException.ThrowIfError(response);

        var affected = ParseAffectedRows(response);
        SyncTrackedEntities(ctx, entityType, setters, filters, orFilters);
        return affected;
    }

    /// <summary>
    /// Sends an asynchronous <c>PATCH</c> request to PostgREST and returns the number
    /// of rows reported affected by the <c>Content-Range</c> response header.
    /// </summary>
    public static async Task<int> ExecuteAsync(
        QueryContext queryContext,
        string tableName,
        IEntityType entityType,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters,
        IReadOnlyList<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)> setters)
    {
        var ctx = (PostgRestQueryContext)queryContext;
        var cancellationToken = queryContext.CancellationToken;

        using var request = BuildRequest(ctx, tableName, filters, orFilters, setters);
        using var response = await ctx.HttpClient
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        await PostgRestException.ThrowIfErrorAsync(response, cancellationToken).ConfigureAwait(false);

        var affected = ParseAffectedRows(response);
        SyncTrackedEntities(ctx, entityType, setters, filters, orFilters);
        return affected;
    }

    /// <summary>
    /// Updates every tracked entry of <paramref name="entityType"/> in the change
    /// tracker to reflect the new column values supplied by the bulk update, then
    /// marks those entries as <see cref="EntityState.Unchanged"/> so EF Core no
    /// longer considers them dirty.
    /// </summary>
    private static void SyncTrackedEntities(
        PostgRestQueryContext ctx,
        IEntityType entityType,
        IReadOnlyList<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)> setters,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters)
    {
        var clrType = entityType.ClrType;
        var properties = entityType.GetProperties().ToList();

        foreach (var entry in ctx.Context.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Detached || !clrType.IsInstanceOfType(entry.Entity))
                continue;

            foreach (var (_, propertyName, value, paramName, isParam) in setters)
            {
                var property = properties.Find(p => p.Name == propertyName);
                if (property is null)
                    continue;

                var entryObject = entry.Entity;

                if (!filters.All(Filter) && !orFilters.Any(r => r.Branches.Any(Filter))) continue;

                var resolved = isParam ? ctx.Parameters[paramName!] : value;

                // Write directly to the CLR object — this is what the caller holds a
                // reference to. entry.Property().CurrentValue alone only updates EF's
                // internal snapshot; setting State=Unchanged afterward would roll it back
                // by re-reading the unmodified CLR property.
                if (property.PropertyInfo is not null) property.PropertyInfo.SetValue(entry.Entity, resolved);
                else property.FieldInfo?.SetValue(entry.Entity, resolved);

                // Also update EF's internal current-value so the entry is consistent.
                entry.Property(propertyName).CurrentValue = resolved;

                bool Filter(PostgRestFilter r)
                {
                    var prop = entry.Property(r.PropertyName);
                    return Equals(prop.OriginalValue, prop.Metadata.PropertyInfo?.GetValue(entryObject));
                }
            }

            // Accept changes: EF re-reads CurrentValue from the CLR object (now updated)
            // and marks original == current, leaving the entry Unchanged with no dirty flag.
            entry.State = EntityState.Unchanged;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildRequest(
        PostgRestQueryContext ctx,
        string tableName,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters,
        IReadOnlyList<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)> setters)
    {
        var url = BuildUrl(ctx, tableName, filters, orFilters);
        var body = SerializeSetters(ctx, setters);

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (ctx.Options.BearerToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (ctx.Options.Schema is { } schema)
        {
            request.Headers.TryAddWithoutValidation("Accept-Profile", schema);
            request.Headers.TryAddWithoutValidation("Content-Profile", schema);
        }

        // Ask PostgREST for the count so we can report rows affected.
        request.Headers.TryAddWithoutValidation("Prefer", "count=exact");

        return request;
    }

    private static string BuildUrl(
        PostgRestQueryContext ctx,
        string tableName,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters)
    {
        var url = $"{ctx.BaseUrl}/{tableName}";
        var queryParams = new List<string>();

        foreach (var filter in filters)
        {
            var value = filter.IsParameter
                ? ctx.Parameters[filter.ParameterName!]
                : filter.Value;
            queryParams.Add(filter.ToQueryStringSegment(value));
        }

        foreach (var orFilter in orFilters)
        {
            var segments = new List<string>(orFilter.Branches.Count);
            foreach (var branch in orFilter.Branches)
            {
                var value = branch.IsParameter
                    ? ctx.Parameters[branch.ParameterName!]
                    : branch.Value;
                segments.Add(branch.ToOrSegment(value));
            }
            queryParams.Add($"or=({string.Join(",", segments)})");
        }

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        return url;
    }

    private static string SerializeSetters(
        PostgRestQueryContext ctx,
        IReadOnlyList<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)> setters)
    {
        var dict = new Dictionary<string, object?>(setters.Count);

        foreach (var (column, _, value, paramName, isParam) in setters)
        {
            var resolved = isParam ? ctx.Parameters[paramName!] : value;
            dict[column] = FormatValue(resolved);
        }

        return JsonSerializer.Serialize(dict);
    }

    private static object? FormatValue(object? value) => value switch
    {
        null => null,
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF"),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };

    /// <summary>
    /// Reads the number of affected rows from the <c>Content-Range</c> header
    /// (e.g. <c>0-4/5</c> → 5). Falls back to -1 if the header is absent.
    /// </summary>
    private static int ParseAffectedRows(HttpResponseMessage response)
    {
        if (response.Content.Headers.TryGetValues("Content-Range", out var values))
        {
            var range = values.FirstOrDefault();
            if (range is not null)
            {
                // Format: "0-N/total" or "*/total"
                var slashIdx = range.IndexOf('/');
                if (slashIdx >= 0 && int.TryParse(range.AsSpan(slashIdx + 1), out var total))
                    return total;
            }
        }

        return -1;
    }
}
