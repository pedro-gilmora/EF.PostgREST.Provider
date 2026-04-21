using System.Net.Http.Json;
using System.Text.Json;

namespace PosgREST.DbContext.Provider.Core;

/// <summary>
/// Exception thrown when a PostgREST HTTP request fails with an error response.
/// Captures the structured error information returned by PostgREST in its JSON
/// error body: <c>message</c>, <c>details</c>, <c>hint</c>, and <c>code</c>.
/// </summary>
/// <remarks>
/// Creates a new <see cref="PostgRestException"/>.
/// </remarks>
public sealed class PostgRestException(
    int statusCode,
    string? postgRestMessage,
    string? details,
    string? hint,
    string? code) : Exception(FormatMessage(statusCode, postgRestMessage, details, hint, code))
{

    /// <summary>The HTTP status code returned by PostgREST.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>The PostgREST error message.</summary>
    public string? PostgRestMessage { get; } = postgRestMessage;

    /// <summary>Additional error details from PostgREST.</summary>
    public string? Details { get; } = details;

    /// <summary>A hint for resolving the error.</summary>
    public string? Hint { get; } = hint;

    /// <summary>The PostgreSQL error code (e.g., <c>23505</c> for unique violation).</summary>
    public string? PostgresCode { get; } = code;

    /// <summary>
    /// Attempts to parse a PostgREST JSON error body and throws a
    /// <see cref="PostgRestException"/> if the response indicates failure.
    /// </summary>
    internal static void ThrowIfError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        string? message = null, details = null, hint = null, code = null;

        try
        {
            using var stream = response.Content.ReadAsStream();
            if (stream.Length > 0)
            {
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("message", out var msgEl))
                    message = msgEl.GetString();
                if (root.TryGetProperty("details", out var detEl))
                    details = detEl.GetString();
                if (root.TryGetProperty("hint", out var hintEl))
                    hint = hintEl.GetString();
                if (root.TryGetProperty("code", out var codeEl))
                    code = codeEl.GetString();
            }
        }
        catch
        {
            // If we can't parse the error body, we'll still throw with the status code.
        }

        throw new PostgRestException(statusCode, message, details, hint, code);
    }

    /// <summary>
    /// Async variant of <see cref="ThrowIfError"/>.
    /// </summary>
    internal static async Task ThrowIfErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        string? message = null, details = null, hint = null, code = null;

        try
        {
            var jsonResponse = await response.Content
                .ReadFromJsonAsync<JsonElement?>(cancellationToken)
                .ConfigureAwait(false);

            if (jsonResponse is { } root)
            {
                if (root.TryGetProperty("message", out var msgEl))
                    message = msgEl.GetString();
                if (root.TryGetProperty("details", out var detEl))
                    details = detEl.GetString();
                if (root.TryGetProperty("hint", out var hintEl))
                    hint = hintEl.GetString();
                if (root.TryGetProperty("code", out var codeEl))
                    code = codeEl.GetString();
            }
        }
        catch (PostgRestException)
        {
            throw;
        }
        catch
        {
            // If we can't parse the error body, we'll still throw with the status code.
        }

        throw new PostgRestException(statusCode, message, details, hint, code);
    }

    private static string FormatMessage(
        int statusCode,
        string? message,
        string? details,
        string? hint,
        string? code)
    {
        var parts = new List<string> { $"PostgREST request failed with status {statusCode}" };

        if (!string.IsNullOrWhiteSpace(message))
            parts.Add(message!);
        if (!string.IsNullOrWhiteSpace(details))
            parts.Add($"Details: {details}");
        if (!string.IsNullOrWhiteSpace(hint))
            parts.Add($"Hint: {hint}");
        if (!string.IsNullOrWhiteSpace(code))
            parts.Add($"PostgreSQL code: {code}");

        return string.Join(". ", parts) + ".";
    }
}
