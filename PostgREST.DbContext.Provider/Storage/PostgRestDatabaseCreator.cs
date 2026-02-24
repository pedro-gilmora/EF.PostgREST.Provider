using Microsoft.EntityFrameworkCore.Storage;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Database creator for the PostgREST provider.
/// PostgREST is a read/write HTTP facade over an existing PostgreSQL database,
/// so schema creation and deletion are not supported from this provider.
/// <see cref="CanConnect"/> / <see cref="CanConnectAsync"/> verify reachability
/// of the PostgREST instance by issuing <c>GET /</c> (the OpenAPI root).
/// </summary>
public sealed class PostgRestDatabaseCreator : IDatabaseCreator
{
    private readonly HttpClient _httpClient;
    private readonly PostgRestDbContextOptionsExtension _options;

    /// <summary>
    /// Creates a new <see cref="PostgRestDatabaseCreator"/> instance.
    /// </summary>
    public PostgRestDatabaseCreator(
        HttpClient httpClient,
        PostgRestDbContextOptionsExtension options)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options
            ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Not supported — PostgREST manages the underlying PostgreSQL schema.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public bool EnsureCreated()
        => throw new NotSupportedException(
            "EnsureCreated is not supported by the PostgREST provider. " +
            "The database schema is managed by PostgreSQL, not PostgREST.");

    /// <summary>
    /// Not supported — PostgREST manages the underlying PostgreSQL schema.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "EnsureCreatedAsync is not supported by the PostgREST provider. " +
            "The database schema is managed by PostgreSQL, not PostgREST.");

    /// <summary>
    /// Not supported — PostgREST manages the underlying PostgreSQL schema.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public bool EnsureDeleted()
        => throw new NotSupportedException(
            "EnsureDeleted is not supported by the PostgREST provider. " +
            "The database schema is managed by PostgreSQL, not PostgREST.");

    /// <summary>
    /// Not supported — PostgREST manages the underlying PostgreSQL schema.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "EnsureDeletedAsync is not supported by the PostgREST provider. " +
            "The database schema is managed by PostgreSQL, not PostgREST.");

    /// <summary>
    /// Checks whether the PostgREST instance is reachable by issuing <c>GET /</c>.
    /// </summary>
    public bool CanConnect()
    {
        try
        {
            using var response = _httpClient.Send(
                new HttpRequestMessage(HttpMethod.Get, _options.BaseUrl),
                HttpCompletionOption.ResponseHeadersRead);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchronously checks whether the PostgREST instance is reachable by issuing <c>GET /</c>.
    /// </summary>
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                _options.BaseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
