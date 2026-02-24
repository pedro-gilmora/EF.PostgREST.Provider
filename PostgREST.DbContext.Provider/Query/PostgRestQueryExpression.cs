using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using PosgREST.DbContext.Provider.Core;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Custom expression node representing a PostgREST query.
/// Captures the target table, filters, ordering, offset, and limit
/// that will be translated into a PostgREST <c>GET</c> request URL.
/// </summary>
public sealed class PostgRestQueryExpression : Expression
{
    /// <summary>
    /// Creates a new query expression targeting the specified entity type.
    /// The table name is resolved from <c>[Table]</c> / <c>ToTable()</c>
    /// configuration, falling back to the CLR type name in lowercase.
    /// </summary>
    public PostgRestQueryExpression(IEntityType entityType)
    {
        EntityType = entityType;
        TableName = entityType.TableName;
    }

    /// <summary>The entity type this query targets.</summary>
    public IEntityType EntityType { get; }

    /// <summary>The PostgREST endpoint table name.</summary>
    public string TableName { get; set; }

    /// <summary>Horizontal filters (<c>?column=op.value</c>).</summary>
    public List<PostgRestFilter> Filters { get; } = [];

    /// <summary>OR filter groups (<c>?or=(cond1,cond2)</c>).</summary>
    public List<PostgRestOrFilter> OrFilters { get; } = [];

    /// <summary>
    /// Vertical filtering columns (<c>?select=col1,col2</c>).
    /// When empty, all columns are returned.
    /// </summary>
    public List<string> SelectColumns { get; } = [];

    /// <summary>Ordering clauses (<c>?order=col.asc,col2.desc</c>).</summary>
    public List<PostgRestOrderByClause> OrderByClauses { get; } = [];

    /// <summary>Pagination offset (<c>?offset=N</c>).</summary>
    public int? Offset { get; set; }

    /// <summary>Parameter name for a runtime-resolved offset value.</summary>
    public string? OffsetParameterName { get; set; }

    /// <summary>Pagination limit (<c>?limit=N</c>).</summary>
    public int? Limit { get; set; }

    /// <summary>Parameter name for a runtime-resolved limit value.</summary>
    public string? LimitParameterName { get; set; }

    /// <inheritdoc />
    public override Type Type => typeof(ValueBuffer);

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

    /// <summary>Adds a filter to this query.</summary>
    public void AddFilter(PostgRestFilter filter) => Filters.Add(filter);

    /// <summary>Adds an OR filter group to this query.</summary>
    public void AddOrFilter(PostgRestOrFilter orFilter) => OrFilters.Add(orFilter);

    /// <summary>Appends an ordering clause.</summary>
    public void AddOrderBy(PostgRestOrderByClause clause) => OrderByClauses.Add(clause);

    /// <summary>Replaces all ordering clauses.</summary>
    public void ClearOrderBy() => OrderByClauses.Clear();
}
