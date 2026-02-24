using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory for creating <see cref="PostgRestQueryableMethodTranslatingExpressionVisitor"/> instances.
/// </summary>
public class PostgRestQueryableMethodTranslatingExpressionVisitorFactory
    : IQueryableMethodTranslatingExpressionVisitorFactory
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _dependencies;

    /// <summary>
    /// Creates a new factory instance.
    /// </summary>
    public PostgRestQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    /// <inheritdoc />
    public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new PostgRestQueryableMethodTranslatingExpressionVisitor(
            _dependencies, queryCompilationContext, subquery: false);
}
