using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PosgREST.DbContext.Provider.Core.Diagnostics;

/// <summary>
/// Extension methods on <see cref="IDiagnosticsLogger{T}"/> that emit
/// structured, EF Core–style log messages for PostgREST HTTP activity.
/// </summary>
public static class PostgRestLoggerExtensions
{
    // ──────────────────────────────────────────────────────────
    //  RequestExecuting  (Debug)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Logs an HTTP request that is about to be sent to PostgREST.
    /// </summary>
    public static void LogRequestExecuting(
        this IDiagnosticsLogger<DbLoggerCategory.Database.Command> diagnostics,
        string method,
        string url,
        string? body)
    {
        var definition = GetOrCreateRequestExecutingDefinition(diagnostics);
        var resolvedBody = body is { Length: > 0 } b ? b : "(none)";

        // ① ILogger path  — used when AddLogging / UseLoggerFactory is configured.
        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, method, url, resolvedBody);
        }
        // ② DbContextLogger path — used by .LogTo().
        //    EventDefinition.Log does NOT forward to DbContextLogger for custom events,
        //    so we must drive it directly by creating an EventData with a message generator.
        else if (diagnostics.DbContextLogger.ShouldLog(definition.EventId, definition.Level))
        {
            diagnostics.DbContextLogger.Log(new EventData(
                definition,
                (_, _) =>
                    $"Executing PostgREST request: {method} {url}{Environment.NewLine}Body: {resolvedBody}"));
        }

        // ③ DiagnosticSource path — for test harnesses / telemetry listeners.
        if (diagnostics.DiagnosticSource.IsEnabled(definition.EventId.Name!))
        {
            diagnostics.DiagnosticSource.Write(
                definition.EventId.Name!,
                new { Method = method, Url = url, Body = resolvedBody });
        }
    }

    // ──────────────────────────────────────────────────────────
    //  RequestExecuted  (Debug / Warning)
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the outcome of an HTTP request to PostgREST.
    /// Non-2xx status codes are logged at <see cref="LogLevel.Warning"/>.
    /// </summary>
    public static void LogRequestExecuted(
        this IDiagnosticsLogger<DbLoggerCategory.Database.Command> diagnostics,
        string method,
        string url,
        int statusCode,
        TimeSpan elapsedMs)
    {
        var definition = GetOrCreateRequestExecutedDefinition(diagnostics, statusCode);

        // ① ILogger path
        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, method, url, statusCode, elapsedMs);
        }
        // ② DbContextLogger path
        else if (diagnostics.DbContextLogger.ShouldLog(definition.EventId, definition.Level))
        {
            diagnostics.DbContextLogger.Log(new EventData(
                definition,
                (_, _) =>
                    $"Executed PostgREST request: {method} {url} — {statusCode} ({elapsedMs.TotalMilliseconds:F1}ms)"));
        }

        // ③ DiagnosticSource path
        if (diagnostics.DiagnosticSource.IsEnabled(definition.EventId.Name!))
        {
            diagnostics.DiagnosticSource.Write(
                definition.EventId.Name!,
                new { Method = method, Url = url, StatusCode = statusCode, ElapsedMs = elapsedMs });
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Definition factories (lazy-init on LoggingDefinitions)
    // ──────────────────────────────────────────────────────────

    private static EventDefinition<string, string, string> GetOrCreateRequestExecutingDefinition(
        IDiagnosticsLogger<DbLoggerCategory.Database.Command> diagnostics)
    {
        var defs = diagnostics.Definitions as PostgRestLoggingDefinitions
            ?? throw new InvalidOperationException(
                $"Expected {nameof(PostgRestLoggingDefinitions)} but found " +
                $"{diagnostics.Definitions.GetType().Name}.");

        if (defs.LogRequestExecuting is not null)
            return defs.LogRequestExecuting;

        var definition = new EventDefinition<string, string, string>(
            diagnostics.Options,
            PostgRestEventId.RequestExecuting,
            LogLevel.Debug,
            "PostgRestEventId.RequestExecuting",
            level => LoggerMessage.Define<string, string, string>(
                level,
                PostgRestEventId.RequestExecuting,
                "Executing PostgREST request: {Method} {Url}" + Environment.NewLine +
                "Body: {Body}"));

        return Interlocked.CompareExchange(ref defs.LogRequestExecuting, definition, null) ?? definition;
    }

    private static EventDefinition<string, string, int, TimeSpan> GetOrCreateRequestExecutedDefinition(
        IDiagnosticsLogger<DbLoggerCategory.Database.Command> diagnostics,
        int statusCode)
    {
        var defs = diagnostics.Definitions as PostgRestLoggingDefinitions
            ?? throw new InvalidOperationException(
                $"Expected {nameof(PostgRestLoggingDefinitions)} but found " +
                $"{diagnostics.Definitions.GetType().Name}.");

        if (defs.LogRequestExecuted is not null)
            return defs.LogRequestExecuted;

        var level = statusCode is >= 200 and <= 299 ? LogLevel.Debug : LogLevel.Warning;

        var definition = new EventDefinition<string, string, int, TimeSpan>(
            diagnostics.Options,
            PostgRestEventId.RequestExecuted,
            level,
            "PostgRestEventId.RequestExecuted",
            l => LoggerMessage.Define<string, string, int, TimeSpan>(
                l,
                PostgRestEventId.RequestExecuted,
                "Executed PostgREST request: {Method} {Url} — {StatusCode} ({ElapsedMs})"));

        return Interlocked.CompareExchange(ref defs.LogRequestExecuted, definition, null) ?? definition;
    }
}

