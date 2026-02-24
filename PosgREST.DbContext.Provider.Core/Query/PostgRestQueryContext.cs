using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Extended <see cref="QueryContext"/> that carries the <see cref="HttpClient"/>
/// and PostgREST base URL needed to execute queries at runtime.
/// </summary>
public class PostgRestQueryContext : QueryContext
{
    /// <summary>
    /// Creates a new <see cref="PostgRestQueryContext"/>.
    /// </summary>
    public PostgRestQueryContext(
        QueryContextDependencies dependencies,
        HttpClient httpClient,
        string baseUrl)
        : base(dependencies)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        BaseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
    }

    /// <summary>The <see cref="System.Net.Http.HttpClient"/> for PostgREST requests.</summary>
    public HttpClient HttpClient { get; }

    /// <summary>The PostgREST base URL (without trailing slash).</summary>
    public string BaseUrl { get; }
}
