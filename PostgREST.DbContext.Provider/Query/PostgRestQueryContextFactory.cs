using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Proxies.Internal;
using Microsoft.EntityFrameworkCore.Query;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Factory that creates <see cref="PostgRestQueryContext"/> instances
/// for query execution.
/// </summary>
/// <remarks>
/// Creates a new factory instance.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Support in this EF Core Provider")]
public class PostgRestQueryContextFactory(
    QueryContextDependencies dependencies,
    HttpClient httpClient,
    PostgRestDbContextOptionsExtension options,
    IDiagnosticsLogger<DbLoggerCategory.Database.Command>? commandLogger,
    IProxyFactory? proxyFactory = null) : IQueryContextFactory
{

    /// <inheritdoc />
    public QueryContext Create()
        => new PostgRestQueryContext(
                dependencies ?? throw new ArgumentNullException(nameof(dependencies)),
                httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
                options ?? throw new ArgumentNullException(nameof(options)),
                commandLogger,
                proxyFactory);
}
