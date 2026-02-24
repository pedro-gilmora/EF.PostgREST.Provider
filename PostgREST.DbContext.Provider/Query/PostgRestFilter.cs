namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Represents a single PostgREST horizontal filter (WHERE clause equivalent).
/// The value is either a compile-time constant or a runtime query parameter.
/// </summary>
public sealed class PostgRestFilter
{
    /// <summary>The PostgREST column name to filter on.</summary>
    public required string Column { get; init; }

    /// <summary>The comparison operator.</summary>
    public required PostgRestFilterOperator Operator { get; init; }

    /// <summary>
    /// When <c>true</c>, the operator is prefixed with <c>not.</c>
    /// (e.g. <c>not.eq</c>, <c>not.is.null</c>).
    /// </summary>
    public bool Negate { get; init; }

    /// <summary>
    /// The constant filter value. Used when <see cref="IsParameter"/> is <c>false</c>.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// The name of the <see cref="Microsoft.EntityFrameworkCore.Query.QueryContext"/> parameter.
    /// Used when <see cref="IsParameter"/> is <c>true</c>.
    /// </summary>
    public string? ParameterName { get; init; }

    /// <summary>
    /// <c>true</c> when the value must be resolved at runtime
    /// from <see cref="Microsoft.EntityFrameworkCore.Query.QueryContext.Parameters"/>.
    /// </summary>
    public bool IsParameter { get; init; }

    /// <summary>
    /// Formats this filter as a PostgREST query-string segment: <c>column=op.value</c>.
    /// </summary>
    public string ToQueryStringSegment(object? resolvedValue)
    {
        var prefix = Negate ? "not." : "";

        if (Operator == PostgRestFilterOperator.In)
        {
            var items = FormatCollectionValue(resolvedValue);
            return $"{Column}={prefix}in.({items})";
        }

        var formattedValue = FormatValue(resolvedValue);
        return $"{Column}={prefix}{Operator.ToPostgRestToken()}.{formattedValue}";
    }

    /// <summary>
    /// Formats this filter as an inner segment for an <c>or=(...)</c> group
    /// (without the column query-string key prefix): <c>column.op.value</c>.
    /// </summary>
    public string ToOrSegment(object? resolvedValue)
    {
        var prefix = Negate ? "not." : "";

        if (Operator == PostgRestFilterOperator.In)
        {
            var items = FormatCollectionValue(resolvedValue);
            return $"{Column}.{prefix}in.({items})";
        }

        var formattedValue = FormatValue(resolvedValue);
        return $"{Column}.{prefix}{Operator.ToPostgRestToken()}.{formattedValue}";
    }

    internal static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm:ss"),
        _ => value.ToString() ?? "null"
    };

    private static string FormatCollectionValue(object? value)
    {
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
                items.Add(FormatValue(item));
            return string.Join(",", items);
        }

        return FormatValue(value);
    }
}

/// <summary>
/// Represents a PostgREST OR filter group: <c>?or=(cond1,cond2,...)</c>.
/// Each branch is a <see cref="PostgRestFilter"/>.
/// </summary>
public sealed class PostgRestOrFilter
{
    /// <summary>The branches of the OR group.</summary>
    public required List<PostgRestFilter> Branches { get; init; }
}
