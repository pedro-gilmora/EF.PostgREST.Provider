using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory for creating <see cref="PostgRestShapedQueryCompilingExpressionVisitor"/> instances.
/// </summary>
/// <remarks>
/// Creates a new factory instance.
/// </remarks>
public class PostgRestShapedQueryCompilingExpressionVisitorFactory(
    ShapedQueryCompilingExpressionVisitorDependencies dependencies)
        : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    /// <inheritdoc />
    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new PostgRestShapedQueryCompilingExpressionVisitor(_dependencies, queryCompilationContext);
}
