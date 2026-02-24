using Microsoft.EntityFrameworkCore.Query;
using PosgREST.DbContext.Provider.Core.Infrastructure;

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
        PostgRestDbContextOptionsExtension options)
        : base(dependencies)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        BaseUrl = options.BaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The <see cref="System.Net.Http.HttpClient"/> for PostgREST requests.</summary>
    public HttpClient HttpClient { get; }

    /// <summary>The PostgREST base URL (without trailing slash).</summary>
    public string BaseUrl { get; }

    /// <summary>The provider options carrying auth token and schema.</summary>
    public PostgRestDbContextOptionsExtension Options { get; }
}
