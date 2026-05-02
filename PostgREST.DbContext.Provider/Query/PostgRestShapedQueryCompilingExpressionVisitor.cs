using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

using PostgREST.DbContext.Provider.Query;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Compiles a <see cref="ShapedQueryExpression"/> containing a
/// <see cref="PostgRestQueryExpression"/> into an expression tree
/// that creates a <see cref="PostgRestQueryingEnumerable{T}"/> at runtime.
/// </summary>
/// <remarks>
/// Creates a new instance.
/// </remarks>
public class PostgRestShapedQueryCompilingExpressionVisitor(ShapedQueryCompilingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext) :
                      ShapedQueryCompilingExpressionVisitor(dependencies, queryCompilationContext)
{
    /// <inheritdoc />
    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var queryExpression = (PostgRestQueryExpression)shapedQueryExpression.QueryExpression;
        var shaperExpression = shapedQueryExpression.ShaperExpression;

        shaperExpression = InjectStructuralTypeMaterializers(shaperExpression);

        if (queryExpression.Projector is null)
        {
            var rootParam = Expression.Parameter(queryExpression.EntityType.ClrType, "instance1");
            shaperExpression = PostgRestMaterializer.Build(queryExpression.EntityType, rootParam);

            //shaperExpression = new ProjectionBindingRemovingVisitor(valueBufferParam).Visit(shaperExpression);

            queryExpression.Projector = Expression.Lambda(
                shaperExpression,
                QueryCompilationContext.QueryContextParameter,
                rootParam);
        }
        else
        {
            queryExpression.Projector = Expression.Lambda(queryExpression.Projector.Body, QueryCompilationContext.QueryContextParameter, queryExpression.Projector.Parameters[0]);
        }

        var enumerableType = typeof(PostgRestQueryingEnumerable<,>).MakeGenericType(queryExpression.EntityType.ClrType, queryExpression.Type);
        var constructor = enumerableType.GetConstructors()[0];
        var colsTreeCopy = new ColumnsTree() { ClrType = queryExpression.EntityType.ClrType, TargetEntity = queryExpression.EntityType };

        foreach (var r in queryExpression.SelectColumns.OrderBy(e => e.IsRelation)) colsTreeCopy.Add(r);

        return Expression.New(
            constructor,
            Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(PostgRestQueryContext)),
            Expression.Constant(queryExpression.TableName),
            Expression.Constant(queryExpression.Filters.ToList(), typeof(IReadOnlyList<PostgRestFilter>)),
            Expression.Constant(queryExpression.OrFilters.ToList(), typeof(IReadOnlyList<PostgRestOrFilter>)),
            Expression.Constant(colsTreeCopy, typeof(ColumnsTree)),
            Expression.Constant(queryExpression.OrderByClauses.ToList(), typeof(IReadOnlyList<PostgRestOrderByClause>)),
            Expression.Constant(queryExpression.Offset, typeof(int?)),
            Expression.Constant(queryExpression.OffsetParameterName, typeof(string)),
            Expression.Constant(queryExpression.Limit, typeof(int?)),
            Expression.Constant(queryExpression.LimitParameterName, typeof(string)),
            queryExpression.Projector);
    }

    private sealed class ProjectionBindingRemovingVisitor(ParameterExpression valueBufferParam) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression) return valueBufferParam;

            return base.VisitExtension(node);
        }
    }
    
}
