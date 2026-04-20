using System.Text.Json;
using Microsoft.EntityFrameworkCore.Query;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Extended <see cref="QueryContext"/> that carries the <see cref="HttpClient"/>
/// and PostgREST base URL needed to execute queries at runtime.
/// </summary>
/// <remarks>
/// Creates a new <see cref="PostgRestQueryContext"/>.
/// </remarks>
public class PostgRestQueryContext(
    QueryContextDependencies dependencies,
    HttpClient httpClient,
    PostgRestDbContextOptionsExtension options) : QueryContext(dependencies)
{

    /// <summary>The <see cref="System.Net.Http.HttpClient"/> for PostgREST requests.</summary>
    public HttpClient HttpClient { get; } = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>The PostgREST base URL (without trailing slash).</summary>
    public string BaseUrl { get; } = options.BaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The provider options carrying auth token and schema.</summary>
    public PostgRestDbContextOptionsExtension Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// The raw JSON element for the row currently being materialized.
    /// Set by the enumerator immediately before invoking the shaper delegate,
    /// so nested-collection shaper expressions can read embedded arrays from it.
    /// </summary>
    public JsonElement CurrentJsonElement { get; set; }
}
