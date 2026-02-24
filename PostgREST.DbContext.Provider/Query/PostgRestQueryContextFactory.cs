using Microsoft.EntityFrameworkCore.Query;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory that creates <see cref="PostgRestQueryContext"/> instances
/// for query execution.
/// </summary>
public class PostgRestQueryContextFactory : IQueryContextFactory
{
    private readonly QueryContextDependencies _dependencies;
    private readonly HttpClient _httpClient;
    private readonly PostgRestDbContextOptionsExtension _options;

    /// <summary>
    /// Creates a new factory instance.
    /// </summary>
    public PostgRestQueryContextFactory(
        QueryContextDependencies dependencies,
        HttpClient httpClient,
        PostgRestDbContextOptionsExtension options)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public QueryContext Create()
        => new PostgRestQueryContext(_dependencies, _httpClient, _options);
}
