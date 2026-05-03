using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
public class PostgRestQueryContextFactory(
    QueryContextDependencies dependencies,
    HttpClient httpClient,
    PostgRestDbContextOptionsExtension options,
    IDiagnosticsLogger<DbLoggerCategory.Database.Command> commandLogger) : IQueryContextFactory
{
    private readonly QueryContextDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly PostgRestDbContextOptionsExtension _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IDiagnosticsLogger<DbLoggerCategory.Database.Command> _commandLogger = commandLogger ?? throw new ArgumentNullException(nameof(commandLogger));

    /// <inheritdoc />
    public QueryContext Create()
        => new PostgRestQueryContext(_dependencies, _httpClient, _options, _commandLogger);
}
