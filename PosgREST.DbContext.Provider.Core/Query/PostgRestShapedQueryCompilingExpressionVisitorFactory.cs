using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory for creating <see cref="PostgRestShapedQueryCompilingExpressionVisitor"/> instances.
/// </summary>
public class PostgRestShapedQueryCompilingExpressionVisitorFactory
    : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;

    /// <summary>
    /// Creates a new factory instance.
    /// </summary>
    public PostgRestShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    /// <inheritdoc />
    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new PostgRestShapedQueryCompilingExpressionVisitor(_dependencies, queryCompilationContext);
}
