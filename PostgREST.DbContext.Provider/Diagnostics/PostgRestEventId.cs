using Microsoft.Extensions.Logging;

namespace PosgREST.DbContext.Provider.Core.Diagnostics;

/// <summary>
/// Event IDs for PostgREST provider diagnostic events, compatible with
/// <see cref="Microsoft.EntityFrameworkCore.Diagnostics.IDiagnosticsLogger{T}"/>.
/// </summary>
/// <remarks>
/// IDs are in the 30 000 range to avoid collisions with EF Core core events
/// (1–9 999) and relational events (10 000–19 999).
/// </remarks>
public static class PostgRestEventId
{
    private const int BaseId = 30_000;

    /// <summary>
    /// Raised just before an HTTP request is sent to the PostgREST endpoint.
    /// Logged at <see cref="LogLevel.Debug"/>.
    /// <para>
    /// The log message includes the HTTP method, the full request URL and,
    /// for write operations (<c>POST</c>, <c>PATCH</c>), the JSON request body.
    /// </para>
    /// </summary>
    public static readonly EventId RequestExecuting = new(BaseId + 0, nameof(RequestExecuting));

    /// <summary>
    /// Raised after an HTTP response is received from the PostgREST endpoint.
    /// Logged at <see cref="LogLevel.Debug"/> on success, <see cref="LogLevel.Warning"/>
    /// on non-2xx status codes.
    /// <para>
    /// The log message includes the HTTP method, full URL, HTTP status code and
    /// the round-trip elapsed time in milliseconds.
    /// </para>
    /// </summary>
    public static readonly EventId RequestExecuted = new(BaseId + 1, nameof(RequestExecuted));
}
