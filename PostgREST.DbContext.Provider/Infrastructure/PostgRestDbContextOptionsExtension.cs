using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PosgREST.DbContext.Provider.Core.Diagnostics;
using PosgREST.DbContext.Provider.Core.Query;
using PosgREST.DbContext.Provider.Core.Storage;

namespace PosgREST.DbContext.Provider.Core.Infrastructure;

/// <summary>
/// EF Core options extension that registers PostgREST provider services
/// and carries provider configuration (base URL, auth token, schema).
/// </summary>
public sealed class PostgRestDbContextOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// Creates a new instance with the specified PostgREST base URL.
    /// </summary>
    public PostgRestDbContextOptionsExtension(string baseUrl)
    {
        BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
    }

    /// <summary>
    /// Copy constructor used when modifying options (immutable-style chaining).
    /// </summary>
    private PostgRestDbContextOptionsExtension(PostgRestDbContextOptionsExtension copyFrom)
    {
        BaseUrl = copyFrom.BaseUrl;
        BearerToken = copyFrom.BearerToken;
        Schema = copyFrom.Schema;
        Timeout = copyFrom.Timeout;
    }

    /// <summary>
    /// The PostgREST instance base URL (e.g., <c>http://localhost:3000</c>).
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Optional JWT bearer token for authenticated requests.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// Optional PostgreSQL schema to target via <c>Accept-Profile</c> / <c>Content-Profile</c> headers.
    /// </summary>
    public string? Schema { get; init; }

    /// <summary>
    /// Optional HTTP request timeout for all PostgREST calls.
    /// When <c>null</c>, the <see cref="HttpClient"/> default is used.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info
        => _info ??= new PostgRestOptionsExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IDatabaseProvider, PostgRestDatabaseProvider>()
            .TryAdd<IDatabase, PostgRestDatabase>()
            .TryAdd<IDatabaseCreator, PostgRestDatabaseCreator>()
            .TryAdd<ITypeMappingSource, PostgRestTypeMappingSource>()
            .TryAdd<LoggingDefinitions, PostgRestLoggingDefinitions>()
            .TryAdd<IQueryContextFactory, PostgRestQueryContextFactory>()
            .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, PostgRestQueryableMethodTranslatingExpressionVisitorFactory>()
            .TryAdd<IShapedQueryCompilingExpressionVisitorFactory,
                PostgRestShapedQueryCompilingExpressionVisitorFactory>()
            .TryAddCoreServices();

        // Register a default HttpClient if the consumer hasn't provided one.
        // Consumers using IHttpClientFactory can replace this with their own instance.
        services.TryAddSingleton(_ =>
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/") };
            if (Timeout is { } timeout)
                client.Timeout = timeout;
            return client;
        });

        services.AddSingleton(this);
        services.AddSingleton(sp =>
            new PostgRestDatabaseDependencies(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<PostgRestDbContextOptionsExtension>()));
    }

    /// <inheritdoc />
    public IDbContextOptionsExtension ApplyDefaults(IDbContextOptions options)
        => this;

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "A PostgREST base URL must be configured. Call 'UsePostgRest(url)' in 'OnConfiguring'.");
        }
    }

    /// <summary>
    /// Returns a copy with the specified bearer token.
    /// </summary>
    public PostgRestDbContextOptionsExtension WithBearerToken(string token)
        => new(this) { BearerToken = token };

    /// <summary>
    /// Returns a copy targeting the specified PostgreSQL schema.
    /// </summary>
    public PostgRestDbContextOptionsExtension WithSchema(string schema)
        => new(this) { Schema = schema };

    /// <summary>
    /// Returns a copy with the specified HTTP request timeout.
    /// </summary>
    public PostgRestDbContextOptionsExtension WithTimeout(TimeSpan timeout)
        => new(this) { Timeout = timeout };

    private sealed class PostgRestOptionsExtensionInfo(
        PostgRestDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private PostgRestDbContextOptionsExtension TypedExtension => extension;

        public override bool IsDatabaseProvider => true;

        public override string LogFragment
            => $"PostgREST: {extension.BaseUrl} ";

        public override int GetServiceProviderHashCode()
            => HashCode.Combine(extension.BaseUrl, extension.BearerToken, extension.Schema, extension.Timeout);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            if (other is not PostgRestOptionsExtensionInfo otherInfo)
                return false;

            var otherExt = otherInfo.TypedExtension;
            return extension.BaseUrl == otherExt.BaseUrl
                   && extension.BearerToken == otherExt.BearerToken
                   && extension.Schema == otherExt.Schema
                   && extension.Timeout == otherExt.Timeout;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["PostgREST:BaseUrl"] = extension.BaseUrl;
            if (extension.BearerToken is not null)
                debugInfo["PostgREST:BearerToken"] = "(set)";
            if (extension.Schema is not null)
                debugInfo["PostgREST:Schema"] = extension.Schema;
            if (extension.Timeout is { } timeout)
                debugInfo["PostgREST:Timeout"] = timeout.ToString();
        }
    }
}
