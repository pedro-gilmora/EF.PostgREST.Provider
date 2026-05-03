using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PosgREST.DbContext.Provider.Core.Diagnostics;

/// <summary>
/// Lazy-initialised <see cref="EventDefinitionBase"/> descriptors for every
/// PostgREST provider diagnostic event.
/// </summary>
/// <remarks>
/// Follows the same pattern as the built-in EF Core logging definitions
/// (e.g. <c>RelationalLoggingDefinitions</c>): one nullable field per event,
/// initialised on first use via <see cref="LoggingDefinitions.GetDefinition"/>.
/// </remarks>
public sealed class PostgRestLoggingDefinitions : LoggingDefinitions
{
    /// <summary>
    /// Definition for <see cref="PostgRestEventId.RequestExecuting"/>.
    /// Message: <c>Executing PostgREST request: {method} {url}{newline}Body: {body}</c>
    /// </summary>
    public EventDefinition<string, string, string>? LogRequestExecuting;

    /// <summary>
    /// Definition for <see cref="PostgRestEventId.RequestExecuted"/>.
    /// Message: <c>Executed PostgREST request: {method} {url} ({statusCode} {elapsed}ms)</c>
    /// </summary>
    public EventDefinition<string, string, int, TimeSpan>? LogRequestExecuted;
}
