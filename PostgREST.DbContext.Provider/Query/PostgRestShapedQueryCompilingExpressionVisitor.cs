using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Compiles a <see cref="ShapedQueryExpression"/> containing a
/// <see cref="PostgRestQueryExpression"/> into an expression tree
/// that creates a <see cref="PostgRestQueryingEnumerable{T}"/> at runtime.
/// </summary>
public class PostgRestShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PostgRestShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext)
        : base(dependencies, queryCompilationContext)
    {
    }

    /// <inheritdoc />
    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var queryExpression = (PostgRestQueryExpression)shapedQueryExpression.QueryExpression;
        var shaperExpression = shapedQueryExpression.ShaperExpression;

        // 1. Replace ProjectionBindingExpression with a ValueBuffer parameter.
        var valueBufferParam = Expression.Parameter(typeof(ValueBuffer), "valueBuffer");
        shaperExpression = new ProjectionBindingRemovingVisitor(valueBufferParam)
            .Visit(shaperExpression);

        // 2. Inject structural type materializers (StructuralTypeShaperExpression → constructor calls).
        shaperExpression = InjectStructuralTypeMaterializers(shaperExpression);

        // 3. Build shaper lambda: (QueryContext, ValueBuffer) → T
        //    Replace any reference to the outer QueryContextParameter with a local parameter
        //    so the shaper can be compiled independently.
        var queryContextParam = Expression.Parameter(typeof(QueryContext), "queryContext");
        shaperExpression = new ReplacingExpressionVisitor(
            [QueryCompilationContext.QueryContextParameter],
            [queryContextParam])
            .Visit(shaperExpression);

        var elementType = shaperExpression.Type;
        var shaperLambda = Expression.Lambda(
            shaperExpression,
            queryContextParam,
            valueBufferParam);

        var compiledShaper = shaperLambda.Compile();

        // 4. Build expression: new PostgRestQueryingEnumerable<T>(context, entityType, ...)
        var enumerableType = typeof(PostgRestQueryingEnumerable<>).MakeGenericType(elementType);
        var constructor = enumerableType.GetConstructors()[0];

        return Expression.New(
            constructor,
            Expression.Convert(
                QueryCompilationContext.QueryContextParameter,
                typeof(PostgRestQueryContext)),
            Expression.Constant(queryExpression.EntityType),
            Expression.Constant(queryExpression.TableName),
            Expression.Constant(queryExpression.Filters.ToList(), typeof(IReadOnlyList<PostgRestFilter>)),
            Expression.Constant(queryExpression.OrFilters.ToList(), typeof(IReadOnlyList<PostgRestOrFilter>)),
            Expression.Constant(queryExpression.SelectColumns.ToList(), typeof(IReadOnlyList<string>)),
            Expression.Constant(queryExpression.OrderByClauses.ToList(), typeof(IReadOnlyList<PostgRestOrderByClause>)),
            queryExpression.Offset is { } offset
                ? Expression.Constant(offset, typeof(int?))
                : Expression.Constant(null, typeof(int?)),
            Expression.Constant(queryExpression.OffsetParameterName, typeof(string)),
            queryExpression.Limit is { } limit
                ? Expression.Constant(limit, typeof(int?))
                : Expression.Constant(null, typeof(int?)),
            Expression.Constant(queryExpression.LimitParameterName, typeof(string)),
            Expression.Constant(compiledShaper));
    }

    /// <summary>
    /// Visitor that replaces <see cref="ProjectionBindingExpression"/> with a
    /// <see cref="ValueBuffer"/> parameter, allowing the shaper to read from it.
    /// </summary>
    private sealed class ProjectionBindingRemovingVisitor(
        ParameterExpression valueBufferParam) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression)
                return valueBufferParam;

            return base.VisitExtension(node);
        }
    }
}
