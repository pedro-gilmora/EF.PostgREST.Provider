using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory for creating <see cref="PostgRestQueryableMethodTranslatingExpressionVisitor"/> instances.
/// </summary>
/// <remarks>
/// Creates a new factory instance.
/// </remarks>
public class PostgRestQueryableMethodTranslatingExpressionVisitorFactory(
    QueryableMethodTranslatingExpressionVisitorDependencies dependencies)
        : IQueryableMethodTranslatingExpressionVisitorFactory
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    /// <inheritdoc />
    public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new PostgRestQueryableMethodTranslatingExpressionVisitor(
            _dependencies, queryCompilationContext, subquery: false);
}
