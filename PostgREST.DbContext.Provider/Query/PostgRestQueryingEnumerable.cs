using System.Collections;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// An <see cref="IEnumerable{T}"/> and <see cref="IAsyncEnumerable{T}"/> that
/// executes a PostgREST <c>GET</c> request and materializes the JSON response
/// into entity instances using the provided shaper delegate.
/// </summary>
public sealed class PostgRestQueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>, IQueryingEnumerable
{
    private readonly PostgRestQueryContext _queryContext;
    private readonly IEntityType _entityType;
    private readonly string _tableName;
    private readonly IReadOnlyList<PostgRestFilter> _filters;
    private readonly IReadOnlyList<PostgRestOrFilter> _orFilters;
    private readonly IReadOnlyList<string> _selectColumns;
    private readonly IReadOnlyList<PostgRestOrderByClause> _orderByClauses;
    private readonly int? _offset;
    private readonly string? _offsetParameterName;
    private readonly int? _limit;
    private readonly string? _limitParameterName;
    private readonly Func<QueryContext, ValueBuffer, T> _shaper;

    /// <summary>
    /// Creates a new querying enumerable.
    /// </summary>
    public PostgRestQueryingEnumerable(
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
        Func<QueryContext, ValueBuffer, T> shaper)
    {
        _queryContext = queryContext;
        _entityType = entityType;
        _tableName = tableName;
        _filters = filters;
        _orFilters = orFilters;
        _selectColumns = selectColumns;
        _orderByClauses = orderByClauses;
        _offset = offset;
        _offsetParameterName = offsetParameterName;
        _limit = limit;
        _limitParameterName = limitParameterName;
        _shaper = shaper;
    }

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
        var url = $"{_queryContext.BaseUrl}/{_tableName}";
        var queryParams = new List<string>();

        // Vertical filtering: ?select=col1,col2
        if (_selectColumns.Count > 0)
            queryParams.Add($"select={string.Join(",", _selectColumns)}");

        // Horizontal filters: ?column=op.value
        foreach (var filter in _filters)
        {
            var value = filter.IsParameter
                ? _queryContext.Parameters[filter.ParameterName!]
                : filter.Value;
            queryParams.Add(filter.ToQueryStringSegment(value));
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
            queryParams.Add($"or=({string.Join(",", segments)})");
        }

        if (_orderByClauses.Count > 0)
        {
            var orderParts = string.Join(",", _orderByClauses);
            queryParams.Add($"order={orderParts}");
        }

        var resolvedOffset = _offset
            ?? (_offsetParameterName is not null
                ? (int?)_queryContext.Parameters[_offsetParameterName]
                : null);
        if (resolvedOffset is { } offset)
            queryParams.Add($"offset={offset}");

        var resolvedLimit = _limit
            ?? (_limitParameterName is not null
                ? (int?)_queryContext.Parameters[_limitParameterName]
                : null);
        if (resolvedLimit is { } limit)
            queryParams.Add($"limit={limit}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        return url;
    }

    private IReadOnlyList<IProperty> GetOrderedProperties()
        => _entityType.GetProperties().ToList();

    private ValueBuffer CreateValueBuffer(JsonElement element, IReadOnlyList<IProperty> properties)
    {
        var values = new object?[properties.Count];

        for (var i = 0; i < properties.Count; i++)
        {
            var prop = properties[i];
            var columnName = prop.Name.ToLowerInvariant();

            if (element.TryGetProperty(columnName, out var jsonProp)
                && jsonProp.ValueKind != JsonValueKind.Undefined)
            {
                values[i] = ConvertJsonValue(jsonProp, prop.ClrType);
            }
        }

        return new ValueBuffer(values);
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
        if (underlying == typeof(JsonElement))
            return element.Clone();

        // Fallback: try to deserialize
        return JsonSerializer.Deserialize(element.GetRawText(), targetType);
    }

    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly PostgRestQueryingEnumerable<T> _enumerable;
        private IReadOnlyList<JsonElement>? _results;
        private IReadOnlyList<IProperty>? _properties;
        private int _index = -1;

        public Enumerator(PostgRestQueryingEnumerable<T> enumerable) => _enumerable = enumerable;

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
                var valueBuffer = _enumerable.CreateValueBuffer(_results[_index], _properties!);
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
            request.Headers.Accept.ParseAdd("application/json");

            using var response = _enumerable._queryContext.HttpClient
                .Send(request, HttpCompletionOption.ResponseContentRead);

            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);

            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : [doc.RootElement.Clone()];
        }
    }

    private sealed class AsyncEnumerator : IAsyncEnumerator<T>
    {
        private readonly PostgRestQueryingEnumerable<T> _enumerable;
        private readonly CancellationToken _cancellationToken;
        private IReadOnlyList<JsonElement>? _results;
        private IReadOnlyList<IProperty>? _properties;
        private int _index = -1;

        public AsyncEnumerator(
            PostgRestQueryingEnumerable<T> enumerable,
            CancellationToken cancellationToken)
        {
            _enumerable = enumerable;
            _cancellationToken = cancellationToken;
        }

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
                var valueBuffer = _enumerable.CreateValueBuffer(_results[_index], _properties!);
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
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _enumerable._queryContext.HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, _cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

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
