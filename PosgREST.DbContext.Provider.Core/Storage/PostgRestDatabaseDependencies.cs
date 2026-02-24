using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Provider-specific dependencies injected into <see cref="PostgRestDatabase"/>.
/// Carries the <see cref="HttpClient"/> and configuration
/// needed to issue HTTP requests against the PostgREST instance.
/// </summary>
public sealed class PostgRestDatabaseDependencies
{
    /// <summary>
    /// Creates a new <see cref="PostgRestDatabaseDependencies"/> instance.
    /// </summary>
    public PostgRestDatabaseDependencies(
        HttpClient httpClient,
        PostgRestDbContextOptionsExtension options)
    {
        HttpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        Options = options
            ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// The <see cref="System.Net.Http.HttpClient"/> used for PostgREST HTTP requests.
    /// The consumer is responsible for configuring and managing its lifetime.
    /// </summary>
    public HttpClient HttpClient { get; }

    /// <summary>
    /// The provider options carrying base URL, auth token, and schema.
    /// </summary>
    public PostgRestDbContextOptionsExtension Options { get; }
}
