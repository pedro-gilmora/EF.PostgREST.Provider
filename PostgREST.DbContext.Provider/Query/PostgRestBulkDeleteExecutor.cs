using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.VisualBasic;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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
public static class PostgRestBulkDeleteExecutor
{
    /// <summary>
    /// Sends a synchronous <c>PATCH</c> request to PostgREST and returns the number
    /// of rows reported affected by the <c>Content-Range</c> response header.
    /// </summary>
    public static int Execute(
        QueryContext queryContext,
        string tableName,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters,
        IEntityType entityType)
    {
        var ctx = (PostgRestQueryContext)queryContext;

        using var request = BuildRequest(ctx, tableName, filters, orFilters);
        using var response = ctx.HttpClient.Send(request, HttpCompletionOption.ResponseContentRead);
        PostgRestException.ThrowIfError(response);

        SyncTrackedEntities(ctx, entityType, filters, orFilters);
       
        var affected = ParseAffectedRows(response);
        return affected;
    }

    /// <summary>
    /// Sends an asynchronous <c>PATCH</c> request to PostgREST and returns the number
    /// of rows reported affected by the <c>Content-Range</c> response header.
    /// </summary>
    public static async Task<int> ExecuteAsync(
        QueryContext queryContext,
        string tableName,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters,
        IEntityType entityType)
    {
        var ctx = (PostgRestQueryContext)queryContext;
        var cancellationToken = queryContext.CancellationToken;

        using var request = BuildRequest(ctx, tableName, filters, orFilters);
        using var response = await ctx.HttpClient
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        await PostgRestException.ThrowIfErrorAsync(response, cancellationToken).ConfigureAwait(false);
       
        SyncTrackedEntities(ctx, entityType, filters, orFilters);
       
        var affected = ParseAffectedRows(response);
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
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters)
    {
        var clrType = entityType.ClrType;

        foreach (var entry in ctx.Context.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Detached || entry.State == EntityState.Deleted || !clrType.IsInstanceOfType(entry.Entity))
                continue;

            var entryObject = entry.Entity;

            if (!filters.All(Filter) && !orFilters.Any(r => r.Branches.Any(Filter))) continue; 

            ctx.Context.Remove(entry.Entity);
            // Accept changes: EF re-reads CurrentValue from the CLR object (now updated)
            // and marks original == current, leaving the entry Unchanged with no dirty flag.
            entry.State = EntityState.Detached;

            bool Filter(PostgRestFilter r)
            {
                var prop = entry.Property(r.PropertyName);
                return Equals(prop.OriginalValue, prop.Metadata.PropertyInfo?.GetValue(entryObject));
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildRequest(
        PostgRestQueryContext ctx,
        string tableName,
        IReadOnlyList<PostgRestFilter> filters,
        IReadOnlyList<PostgRestOrFilter> orFilters)
    {
        var url = BuildUrl(ctx, tableName, filters, orFilters);

        var request = new HttpRequestMessage(HttpMethod.Delete, url);

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
        PostgRestQueryContext _queryContext,
        string tableName,
        IReadOnlyList<PostgRestFilter> _filters,
        IReadOnlyList<PostgRestOrFilter> _orFilters)
    {
        StringBuilder urlBuilder = new();

        // Horizontal filters: ?column=op.value
        foreach (var filter in _filters)
        {
            var value = filter.IsParameter
                ? _queryContext.Parameters[filter.ParameterName!]
                : filter.Value;
            urlBuilder.Append(GetSeparator()).Append(filter.ToQueryStringSegment(value));
        }

        // OR filter groups: ?or=(cond1,cond2)
        foreach (var orFilter in _orFilters)
        {
            var segments = new List<string>(orFilter.Branches.Count);
            foreach (var branch in orFilter.Branches)
            {
                var value = branch.IsParameter
                    ? _queryContext.Parameters[branch.ParameterName!]
                    : branch.Value;
                segments.Add(branch.ToOrSegment(value));
            }
            urlBuilder.Append(GetSeparator()).Append("or=(").AppendJoin(",", segments).Append(')');
        }

        urlBuilder.Insert(0, $"{_queryContext.BaseUrl}/{tableName}");

        return urlBuilder.ToString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        char GetSeparator() => urlBuilder.Length > 0 ? '&' : '?';
    }

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
