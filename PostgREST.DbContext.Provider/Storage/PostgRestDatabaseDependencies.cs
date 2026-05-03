using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Provider-specific dependencies injected into <see cref="PostgRestDatabase"/>.
/// Carries the <see cref="HttpClient"/>, configuration and the diagnostics logger
/// needed to issue HTTP requests against the PostgREST instance.
/// </summary>
/// <remarks>
/// Creates a new <see cref="PostgRestDatabaseDependencies"/> instance.
/// </remarks>
public sealed class PostgRestDatabaseDependencies(
    HttpClient httpClient,
    PostgRestDbContextOptionsExtension options,
    IDiagnosticsLogger<DbLoggerCategory.Database.Command> commandLogger)
{
    /// <summary>
    /// The <see cref="System.Net.Http.HttpClient"/> used for PostgREST HTTP requests.
    /// The consumer is responsible for configuring and managing its lifetime.
    /// </summary>
    public HttpClient HttpClient { get; } = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>
    /// The provider options carrying base URL, auth token, and schema.
    /// </summary>
    public PostgRestDbContextOptionsExtension Options { get; } = options
            ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Diagnostics logger used to emit EF Core–style log messages for
    /// each HTTP request issued to the PostgREST endpoint.
    /// </summary>
    public IDiagnosticsLogger<DbLoggerCategory.Database.Command> CommandLogger { get; } = commandLogger
            ?? throw new ArgumentNullException(nameof(commandLogger));
}
