using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Options;

using PosgREST.DbContext.Provider.Core.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Extended <see cref="QueryContext"/> that carries the <see cref="HttpClient"/>,
/// PostgREST base URL and diagnostics logger needed to execute queries at runtime.
/// </summary>
/// <remarks>
/// Creates a new <see cref="PostgRestQueryContext"/>.
/// </remarks>
public class PostgRestQueryContext(
    QueryContextDependencies dependencies,
    HttpClient httpClient,
    PostgRestDbContextOptionsExtension options,
    IDiagnosticsLogger<DbLoggerCategory.Database.Command> commandLogger) : QueryContext(dependencies)
{
    /// <summary>The <see cref="System.Net.Http.HttpClient"/> for PostgREST requests.</summary>
    public HttpClient HttpClient { get; } = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>The PostgREST base URL (without trailing slash).</summary>
    public string BaseUrl { get; } = options.BaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The provider options carrying auth token and schema.</summary>
    public PostgRestDbContextOptionsExtension Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Diagnostics logger for emitting EF Core–style log messages for each
    /// HTTP GET request issued to the PostgREST endpoint.
    /// </summary>
    public IDiagnosticsLogger<DbLoggerCategory.Database.Command> CommandLogger { get; } = commandLogger
        ?? throw new ArgumentNullException(nameof(commandLogger));
}