using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

using System.Collections;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// An <see cref="IEnumerable{T}"/> and <see cref="IAsyncEnumerable{T}"/> that
/// executes a PostgREST <c>GET</c> request and materializes the JSON response
/// into entity instances using the provided shaper delegate.
/// </summary>
/// <remarks>
/// Creates a new querying enumerable.
/// </remarks>
public sealed class PostgRestQueryingEnumerable<T>(
    PostgRestQueryContext queryContext,
    IEntityType entityType,
    string tableName,
    IReadOnlyList<PostgRestFilter> filters,
    IReadOnlyList<PostgRestOrFilter> orFilters,
    IReadOnlyList<string> selectColumns,
    IReadOnlyList<PostgRestOrderByClause> orderByClauses,
    int? offset,
    string? offsetParameterName,
    int? limit,
    string? limitParameterName,
    Func<QueryContext, ValueBuffer, T> shaper) : IEnumerable<T>, IAsyncEnumerable<T>, IQueryingEnumerable
{
    private readonly PostgRestQueryContext _queryContext = queryContext;
    private readonly IEntityType _entityType = entityType;
    private readonly string _tableName = tableName;
    private readonly IReadOnlyList<PostgRestFilter> _filters = filters;
    private readonly IReadOnlyList<PostgRestOrFilter> _orFilters = orFilters;
    private readonly IReadOnlyList<string> _selectColumns = selectColumns;
    private readonly IReadOnlyList<PostgRestOrderByClause> _orderByClauses = orderByClauses;
    private readonly int? _offset = offset;
    private readonly string? _offsetParameterName = offsetParameterName;
    private readonly int? _limit = limit;
    private readonly string? _limitParameterName = limitParameterName;
    private readonly Func<QueryContext, ValueBuffer, T> _shaper = shaper;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new AsyncEnumerator(this, cancellationToken);

    /// <inheritdoc />
    public string ToQueryString() => BuildUrl();

    private string BuildUrl()
    {
        StringBuilder urlBuilder = new ();

        // Vertical filtering: ?select=col1,col2
        if (_selectColumns.Count > 0)
            urlBuilder.Append(GetSeparator()).Append("select=").AppendJoin(",", _selectColumns);

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
            var orderParts = string.Join(",", _orderByClauses);
            urlBuilder.Append(GetSeparator()).Append("order=").Append(orderParts);
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

    private IReadOnlyList<IProperty> GetOrderedProperties()=> [.. _entityType.GetProperties()];

    private static ValueBuffer CreateValueBuffer(JsonElement element, IReadOnlyList<IProperty> properties)
    {
        var values = new object?[properties.Count];

        for (var i = 0; i < properties.Count; i++)
        {
            var prop = properties[i];
            var columnName = prop.ColumnName;

            if (element.TryGetProperty(columnName, out var jsonProp)
                && jsonProp.ValueKind != JsonValueKind.Undefined)
            {
                values[i] = ConvertJsonValue(jsonProp, prop.ClrType);
            }
        }

        return new ValueBuffer(values);
    }

    private static object? ConvertJsonValue(JsonElement element, Type targetType)
        => PostgRestNestedCollectionHelper.ConvertJsonValue(element, targetType);

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

    private sealed class Enumerator(PostgRestQueryingEnumerable<T> enumerable) : IEnumerator<T>
    {
        private readonly PostgRestQueryingEnumerable<T> _enumerable = enumerable;
        private IReadOnlyList<JsonElement>? _results;
        private IReadOnlyList<IProperty>? _properties;
        private int _index = -1;

        public T Current { get; private set; } = default!;
        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            if (_results is null)
            {
                _enumerable._queryContext.InitializeStateManager(standAlone: false);
                _results = FetchResults();
                _properties = _enumerable.GetOrderedProperties();
            }

            if (++_index < _results.Count)
            {
                var element = _results[_index];
                _enumerable._queryContext.CurrentJsonElement = element;
                var valueBuffer = PostgRestQueryingEnumerable<T>.CreateValueBuffer(element, _properties!);
                Current = _enumerable._shaper(_enumerable._queryContext, valueBuffer);
                return true;
            }

            return false;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }

        private IReadOnlyList<JsonElement> FetchResults()
        {
            var url = _enumerable.BuildUrl();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, _enumerable._queryContext);

            using var response = _enumerable._queryContext.HttpClient
                .Send(request, HttpCompletionOption.ResponseContentRead);

            PostgRestException.ThrowIfError(response);

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : [doc.RootElement.Clone()];
        }
    }

    private sealed class AsyncEnumerator(
        PostgRestQueryingEnumerable<T> enumerable,
        CancellationToken cancellationToken) : IAsyncEnumerator<T>
    {
        private readonly PostgRestQueryingEnumerable<T> _enumerable = enumerable;
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private IReadOnlyList<JsonElement>? _results;
        private IReadOnlyList<IProperty>? _properties;
        private int _index = -1;

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_results is null)
            {
                _enumerable._queryContext.InitializeStateManager(standAlone: false);
                _results = await FetchResultsAsync().ConfigureAwait(false);
                _properties = _enumerable.GetOrderedProperties();
            }

            if (++_index < _results.Count)
            {
                var element = _results[_index];
                _enumerable._queryContext.CurrentJsonElement = element;
                var valueBuffer = PostgRestQueryingEnumerable<T>.CreateValueBuffer(element, _properties!);
                Current = _enumerable._shaper(_enumerable._queryContext, valueBuffer);
                return true;
            }

            return false;
        }

        public ValueTask DisposeAsync() => default;

        private async Task<IReadOnlyList<JsonElement>> FetchResultsAsync()
        {
            var url = _enumerable.BuildUrl();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, _enumerable._queryContext);

            using var response = await _enumerable._queryContext.HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, _cancellationToken)
                .ConfigureAwait(false);

            await PostgRestException.ThrowIfErrorAsync(response, _cancellationToken)
                .ConfigureAwait(false);

            using var stream = await response.Content.ReadAsStreamAsync(_cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: _cancellationToken)
                .ConfigureAwait(false);

            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : [doc.RootElement.Clone()];
        }
    }
}
