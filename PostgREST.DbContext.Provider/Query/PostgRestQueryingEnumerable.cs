using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Proxies.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

using PosgREST.DbContext.Provider.Core.Diagnostics;

using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// An <see cref="IEnumerable{T}"/> and <see cref="IAsyncEnumerable{T}"/> that
/// executes a PostgREST <c>GET</c> request and materializes the JSON response
/// into entity instances using the provided shaper delegate.
/// </summary>
/// <remarks>
/// Creates a new querying enumerable.
/// </remarks>
public sealed class PostgRestQueryingEnumerable<TIn, TOut>(
    PostgRestQueryContext _queryContext,
    string _tableName,
    IReadOnlyList<PostgRestFilter> _filters,
    IReadOnlyList<PostgRestOrFilter> _orFilters,
    ColumnsTree _selectColumns,
    IReadOnlyList<PostgRestOrderByClause> _orderByClauses,
    int? _offset,
    string? _offsetParameterName,
    int? _limit,
    string? _limitParameterName,
    Func<QueryContext, TIn, TOut> _selector) : IEnumerable<TOut>, IAsyncEnumerable<TOut>, IQueryingEnumerable
{
    /// <inheritdoc />
    public IEnumerator<TOut> GetEnumerator() => new Enumerator(_queryContext, _selector, BuildUrl, GetJsonOptions());

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IAsyncEnumerator<TOut> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new AsyncEnumerator(_queryContext, _selector, BuildUrl, GetJsonOptions(), cancellationToken);

    /// <inheritdoc />
    public string ToQueryString() => BuildUrl();

    JsonSerializerOptions GetJsonOptions()
    {
        var context = new Dictionary<Type, IJsonTypeInfoResolver>();

        var options = new JsonSerializerOptions(JsonSerializerDefaults.General);

        GetTopologicalRelatedEntities(_selectColumns);

        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(context.Values.Concat([new DefaultJsonTypeInfoResolver()]).ToArray());

        void GetTopologicalRelatedEntities(ColumnsTree col)
        {
            ref var item = ref CollectionsMarshal.GetValueRefOrAddDefault(context, col.ClrType, out var exists);

            if (exists) return;

            item = new JsonEntityTypeInfoResolver(col);

            bool hasColumns = false;

            foreach (var col2 in col)
            {
                if (col2.IsRelation)
                {
                    if (!hasColumns)
                    {
                        hasColumns = true;
                        item = new JsonFullEntityTypeInfoResolver(col, _queryContext.Context, _queryContext.ProxyFactory);
                    }

                    GetTopologicalRelatedEntities(col2);
                }
                else if (!hasColumns) hasColumns = true;
            }

            if (!hasColumns)
            {
                item = new JsonFullEntityTypeInfoResolver(col, _queryContext.Context, _queryContext.ProxyFactory);
            }
        }

        return options;
    }

    private string BuildUrl()
    {
        StringBuilder urlBuilder = new();

        _selectColumns.Process(urlBuilder.Append(GetSeparator()).Append("select="));

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

        if (_orderByClauses.Count > 0)
        {
            urlBuilder.Append(GetSeparator()).Append("order=").AppendJoin(",", _orderByClauses);
        }

        var resolvedOffset = _offset
            ?? (_offsetParameterName is not null
                ? (int?)_queryContext.Parameters[_offsetParameterName]
                : null);

        if (resolvedOffset is { } offset)
            urlBuilder.Append(GetSeparator()).Append("offset=").Append(offset);

        var resolvedLimit = _limit ?? (_limitParameterName is not null
                ? (int?)_queryContext.Parameters[_limitParameterName]
                : null);

        if (resolvedLimit is { } limit)
            urlBuilder.Append(GetSeparator()).Append("limit=").Append(limit);

        urlBuilder.Insert(0, $"{_queryContext.BaseUrl}/{_tableName}");

        return urlBuilder.ToString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        char GetSeparator() => urlBuilder.Length > 0 ? '&' : '?';
    }

    /// <summary>
    /// Applies common HTTP headers (Accept, Authorization, Schema profiles)
    /// to the given request based on the current query context options.
    /// </summary>
    private static void ApplyHeaders(HttpRequestMessage request, PostgRestQueryContext context)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (context.Options.BearerToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (context.Options.Schema is { } schema)
            request.Headers.TryAddWithoutValidation("Accept-Profile", schema);
    }

    private sealed class Enumerator(PostgRestQueryContext queryContext, Func<QueryContext, TIn, TOut> select, Func<string> buildUrl, JsonSerializerOptions jsonSerializerOptions) : IEnumerator<TOut>, ICurrent
    {
        private IEnumerator<TIn>? _results;

        public TOut Current { get; private set; } = default!;

        TOut ICurrent.Current => Current!;

        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            if (_results is null)
            {
                queryContext.InitializeStateManager(standAlone: false);

                if (FetchResults() is not { } result) return false;

                _results = result.GetEnumerator();
            }

            if (_results.MoveNext())
            {
                Current = select(queryContext, _results.Current);
                return true;
            }

            return false;
        }

        public void Reset() => _results?.Reset();

        public void Dispose() { _results?.Dispose(); }

        private IEnumerable<TIn>? FetchResults()
        {
            var url = buildUrl();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, queryContext);

#if DEBUG
            queryContext.CommandLogger.LogRequestExecuting(HttpMethod.Get.Method, url, body: null);

            var sw = Stopwatch.GetTimestamp();
#endif

            using var response = queryContext.HttpClient
                .Send(request, HttpCompletionOption.ResponseContentRead);

#if DEBUG
            queryContext.CommandLogger.LogRequestExecuted(
                HttpMethod.Get.Method,
                url,
                (int)response.StatusCode,
                Stopwatch.GetElapsedTime(sw));
#endif


            PostgRestException.ThrowIfError(response);

            using var stream = response.Content.ReadAsStream();
            return JsonSerializer.Deserialize<IEnumerable<TIn>>(stream, jsonSerializerOptions);
        }
    }

    private sealed class AsyncEnumerator(
        PostgRestQueryContext queryContext,
        Func<QueryContext, TIn, TOut> selector,
        Func<string> buildUrl,
        JsonSerializerOptions jsonSerializerOptions,
        CancellationToken cancellationToken) : IAsyncEnumerator<TOut>, ICurrent
    {
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private ConfiguredCancelableAsyncEnumerable<TIn>.Enumerator _results = default;

        bool init = false;

        public TOut Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (!init)
            {
                init = true;
                queryContext.InitializeStateManager(standAlone: false);
                await FetchResultsAsync();
            }

            if (await _results.MoveNextAsync())
            {
                Current = selector(queryContext, _results.Current);
                return true;
            }

            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (init) await _results.DisposeAsync();
        }

        private async Task FetchResultsAsync()
        {
            var url = buildUrl();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, queryContext);

#if DEBUG
            queryContext.CommandLogger.LogRequestExecuting(HttpMethod.Get.Method, url, body: null);

            var sw = Stopwatch.GetTimestamp();
#endif
            var response = await queryContext.HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, _cancellationToken)
                .ConfigureAwait(false);

#if DEBUG
            queryContext.CommandLogger.LogRequestExecuted(
                HttpMethod.Get.Method,
                url,
                (int)response.StatusCode,
                Stopwatch.GetElapsedTime(sw));
#endif

            await PostgRestException.ThrowIfErrorAsync(response, _cancellationToken)
                .ConfigureAwait(false);

            _results = response.Content.ReadFromJsonAsAsyncEnumerable<TIn>(jsonSerializerOptions, _cancellationToken)!.ConfigureAwait<TIn>(false).GetAsyncEnumerator();
        }
    }

    private interface ICurrent
    {
        TOut Current { get; }
    }

    class JsonEntityTypeInfoResolver(ColumnsTree column) : IJsonTypeInfoResolver
    {
        private JsonTypeInfo? typeInfo;

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != column.ClrType)
                return null;

            if (typeInfo != null) return typeInfo;

            typeInfo = JsonTypeInfo.CreateJsonTypeInfo(column.ClrType, options);

            typeInfo.CreateObject ??= () => Activator.CreateInstance(column.ClrType)!;

            foreach (var prop in column)
            {
                var jsonProp = typeInfo.CreateJsonPropertyInfo(prop.CollectionType ?? prop.ClrType, prop.Identifier);

                jsonProp.Get = prop.GetValue;

                jsonProp.Set = prop.SetValue;

                typeInfo.Properties.Add(jsonProp);
            }
            return typeInfo;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "<Pending>")]
    class JsonFullEntityTypeInfoResolver(ColumnsTree column, Microsoft.EntityFrameworkCore.DbContext ctx, IProxyFactory? proxyFactory) : IJsonTypeInfoResolver
    {
        private JsonTypeInfo? typeInfo;

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != column.ClrType)
                return null;

            if (typeInfo != null) return typeInfo;

            typeInfo = JsonTypeInfo.CreateJsonTypeInfo(column.ClrType, options);

            typeInfo.CreateObject ??= proxyFactory is null 
                ? () => Activator.CreateInstance(column.ClrType)! 
                : () => proxyFactory.CreateProxy(ctx, column.TargetEntity, []);

            foreach (var targetMember in column.ClrType.GetMembers())
            {
                if (targetMember is not { MemberType: MemberTypes.Field or MemberTypes.Property } || column.TargetEntity.FindMember(targetMember.Name)?.ColumnName is not { } columnsName) continue;

                var (memberType, getValue, setValue) = targetMember switch
                {
                    PropertyInfo { CanWrite: true } p => (p.PropertyType, p.GetValue, p.CanWrite ? p.SetValue : null),
                    FieldInfo f when !(f.IsStatic || f.IsLiteral || f.IsInitOnly) => (f.FieldType, (Func<object, object?>?)f.GetValue, (Action<object, object?>?)f.SetValue),
                    _ => throw new MissingMemberException($"Member for {targetMember} not found")
                };

                var jsonProp = typeInfo.CreateJsonPropertyInfo(memberType, columnsName);

                jsonProp.Get = getValue;

                jsonProp.Set = setValue;

                typeInfo.Properties.Add(jsonProp);
            }

            return typeInfo;
        }
    }
}
