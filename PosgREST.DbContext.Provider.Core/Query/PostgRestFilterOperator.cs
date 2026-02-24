namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// PostgREST horizontal filtering operators.
/// Each value maps to a PostgREST query-string operator token.
/// </summary>
public enum PostgRestFilterOperator
{
    /// <summary><c>eq</c> — equals.</summary>
    Equal,

    /// <summary><c>neq</c> — not equals.</summary>
    NotEqual,

    /// <summary><c>gt</c> — greater than.</summary>
    GreaterThan,

    /// <summary><c>gte</c> — greater than or equal.</summary>
    GreaterThanOrEqual,

    /// <summary><c>lt</c> — less than.</summary>
    LessThan,

    /// <summary><c>lte</c> — less than or equal.</summary>
    LessThanOrEqual,

    /// <summary><c>like</c> — pattern match (case-sensitive).</summary>
    Like,

    /// <summary><c>ilike</c> — pattern match (case-insensitive).</summary>
    ILike,

    /// <summary><c>is</c> — null / true / false check.</summary>
    Is,

    /// <summary><c>in</c> — contained in a list.</summary>
    In
}

/// <summary>
/// Extension methods for <see cref="PostgRestFilterOperator"/>.
/// </summary>
public static class PostgRestFilterOperatorExtensions
{
    /// <summary>
    /// Returns the PostgREST query-string token for this operator.
    /// </summary>
    public static string ToPostgRestToken(this PostgRestFilterOperator op) => op switch
    {
        PostgRestFilterOperator.Equal => "eq",
        PostgRestFilterOperator.NotEqual => "neq",
        PostgRestFilterOperator.GreaterThan => "gt",
        PostgRestFilterOperator.GreaterThanOrEqual => "gte",
        PostgRestFilterOperator.LessThan => "lt",
        PostgRestFilterOperator.LessThanOrEqual => "lte",
        PostgRestFilterOperator.Like => "like",
        PostgRestFilterOperator.ILike => "ilike",
        PostgRestFilterOperator.Is => "is",
        PostgRestFilterOperator.In => "in",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };
}
