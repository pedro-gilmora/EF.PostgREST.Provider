using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

using PosgREST.DbContext.Provider.Core;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Captures metadata for an eager-loaded navigation property (<c>Include</c>).
/// </summary>
/// <param name="Navigation">The EF Core navigation descriptor.</param>
/// <param name="TableName">The PostgREST endpoint name for the related entity.</param>
public sealed record IncludeInfo(INavigation Navigation, string TableName);

/// <summary>
/// Custom expression node representing a PostgREST query.
/// Captures the target table, filters, ordering, offset, and limit
/// that will be translated into a PostgREST <c>GET</c> request URL.
/// </summary>
/// <remarks>
/// Creates a new query expression targeting the specified entity type.
/// The table name is resolved from <c>[Table]</c> / <c>ToTable()</c>
/// configuration, falling back to the CLR type name in lowercase.
/// </remarks>
public sealed class PostgRestQueryExpression(IEntityType entityType) : Expression
{
    /// <summary>The entity type this query targets.</summary>
    public IEntityType EntityType { get; } = entityType;

    /// <summary>The PostgREST endpoint table name.</summary>
    public string TableName { get; set; } = entityType.TableName;

    /// <summary>Horizontal filters (<c>?column=op.value</c>).</summary>
    public List<PostgRestFilter> Filters { get; } = [];

    /// <summary>OR filter groups (<c>?or=(cond1,cond2)</c>).</summary>
    public List<PostgRestOrFilter> OrFilters { get; } = [];

    /// <summary>
    /// Vertical filtering columns (<c>?select=col1,col2</c>).
    /// When empty, all columns are returned.
    /// </summary>
    public ColumnsTree SelectColumns { get; } = [];

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

    internal Type _type = entityType.ClrType;
    /// <inheritdoc />
    public override Type Type => _type;

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    public LambdaExpression? Projector { get; internal set; }

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

public class ColumnsTree(string? identifier = null, bool isRelation = false) : HashSet<ColumnsTree>(new ColumnsComparer())
{
    public string Identifier { get; set; } = identifier!;

    public bool IsRelation { get; set; } = isRelation;

    public bool IsCollection { get; internal set; }

    public Func<object, object?>? GetValue { get; internal set; }

    public Action<object, object?>? SetValue { get; internal set; }
    
#pragma warning disable CS8618 
    public IEntityType OwningEntity { get; internal set; }

    public Type ClrType { get; internal set; }
    public Type? CollectionType { get; internal set; }

#pragma warning restore CS8618

    public void Process(StringBuilder sb)
    {
        var addComma = false;

        var hasScalarColumns = false;

        foreach (var item in this.OrderBy(i => i.IsRelation))
        {
            if (item.Identifier is null) continue;

            if (addComma) sb.Append(','); else addComma = true;

            if (item.IsRelation)
            {
                if (!hasScalarColumns) { hasScalarColumns = true; sb.Append("*,"); }
            }
            else
                hasScalarColumns = true;

            sb.Append(item.Identifier);

            if (!item.IsRelation) continue;

            sb.Append('(');
            if (item.Count > 0) item.Process(sb); else sb.Append('*');
            sb.Append(')');
        }
    }
}

internal class ColumnsComparer : IEqualityComparer<ColumnsTree>
{
    public bool Equals(ColumnsTree? x, ColumnsTree? y)
    {
        return ReferenceEquals(x, y) || Equals(x?.Identifier, y?.Identifier);
    }

    public int GetHashCode([DisallowNull] ColumnsTree obj)
    {
        return obj.Identifier.GetHashCode();
    }
}