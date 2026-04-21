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
/// <remarks>
/// Creates a new instance.
/// </remarks>
public class PostgRestShapedQueryCompilingExpressionVisitor(ShapedQueryCompilingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext) : ShapedQueryCompilingExpressionVisitor(dependencies, queryCompilationContext)
{

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

        // 4. Rewrite nested Enumerable.ToList(Queryable.Select(EntityQueryRoot, lambda))
        //    patterns into PostgRestNestedCollectionHelper.Read<TEntity, TDto>(...) calls
        //    so they are satisfied at runtime from the embedded JSON array on the parent row.
        shaperExpression = new NestedCollectionRewritingVisitor(queryContextParam)
            .Visit(shaperExpression)!;

        var elementType = shaperExpression.Type;
        var shaperLambda = Expression.Lambda(
            shaperExpression,
            queryContextParam,
            valueBufferParam);

        var compiledShaper = shaperLambda.Compile();

        // 5. Build expression: new PostgRestQueryingEnumerable<T>(context, entityType, ...)
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
            Expression.Constant(queryExpression.Offset, typeof(int?)),
            Expression.Constant(queryExpression.OffsetParameterName, typeof(string)),
            Expression.Constant(queryExpression.Limit, typeof(int?)),
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

    /// <summary>
    /// Rewrites <c>Enumerable.ToList(Queryable.Select(Queryable.Where(EntityQueryRoot, …), lambda))</c>
    /// into <c>PostgRestNestedCollectionHelper.Read&lt;TEntity, TDto&gt;(queryContext, entityType, propertyName, selector)</c>
    /// so the collection is populated from the embedded JSON array on the parent row at runtime.
    /// </summary>
    private sealed class NestedCollectionRewritingVisitor(ParameterExpression queryContextParam) : ExpressionVisitor
    {
        // Enumerable.ToList<T>(IEnumerable<T>)
        private static readonly MethodInfo _toListOpen =
            typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.ToList) && m.GetParameters().Length == 1);

        // PostgRestNestedCollectionHelper.Read<TEntity, TDto>(QueryContext, IEntityType, string, Func<TEntity,TDto>)
        private static readonly MethodInfo _readOpen =
            typeof(PostgRestNestedCollectionHelper).GetMethod(nameof(PostgRestNestedCollectionHelper.Read))!;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Match: Enumerable.ToList(Queryable.Select(source, lambda))
            if (node.Method.IsGenericMethod
                && node.Method.GetGenericMethodDefinition() == _toListOpen
                && node.Arguments[0] is MethodCallExpression selectCall
                && selectCall.Method.Name == "Select")
            {
                // Unwrap the inner Select — strip any intermediate Where / other Queryable calls
                // until we reach an EntityQueryRootExpression.
                var selectSource = selectCall.Arguments[0];
                while (selectSource is MethodCallExpression mc && selectSource is not EntityQueryRootExpression)
                    selectSource = mc.Arguments[0];

                if (selectSource is EntityQueryRootExpression root)
                {
                    // Retrieve the projection lambda (may be quoted)
                    var lambdaArg = selectCall.Arguments[^1];
                    var lambda = lambdaArg is UnaryExpression { NodeType: ExpressionType.Quote } q
                        ? (LambdaExpression)q.Operand
                        : (LambdaExpression)lambdaArg;

                    var entityType = root.EntityType;
                    var entityClrType = entityType.ClrType;
                    var dtoType = lambda.ReturnType;

                    // JSON property name = table name (PostgREST embedded resource key)
                    var jsonPropName = entityType.TableName;

                    // Build: PostgRestNestedCollectionHelper.Read<TEntity, TDto>(
                    //            queryContextParam, entityTypeConst, propNameConst, selectorDelegate)
                    var readMethod = _readOpen.MakeGenericMethod(entityClrType, dtoType);

                    // Compile the selector as a delegate constant so the lambda body
                    // (which references raw entity properties) is closed over correctly.
                    var compiled = Expression.Constant(lambda.Compile());

                    return Expression.Call(
                        null,
                        readMethod,
                        queryContextParam,
                        Expression.Constant(entityType),
                        Expression.Constant(jsonPropName),
                        compiled);
                }
            }

            return base.VisitMethodCall(node);
        }
    }
}
