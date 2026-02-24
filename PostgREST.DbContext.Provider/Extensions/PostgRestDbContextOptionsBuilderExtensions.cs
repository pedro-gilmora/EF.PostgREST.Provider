using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Extensions;

/// <summary>
/// Extension methods on <see cref="DbContextOptionsBuilder"/> for configuring
/// the PostgREST EF Core provider.
/// </summary>
public static class PostgRestDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to use the PostgREST provider, targeting the
    /// specified <paramref name="baseUrl"/> (e.g., <c>http://localhost:3000</c>).
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="baseUrl">The PostgREST instance base URL.</param>
    /// <param name="postgRestOptionsAction">
    /// An optional action to further configure PostgREST-specific options
    /// such as bearer token, schema, and timeout via
    /// <see cref="PostgRestDbContextOptionsBuilder"/>.
    /// </param>
    /// <returns>The same <see cref="DbContextOptionsBuilder"/> so that additional configuration can be chained.</returns>
    public static DbContextOptionsBuilder UsePostgRest(
        this DbContextOptionsBuilder optionsBuilder,
        string baseUrl,
        Action<PostgRestDbContextOptionsBuilder>? postgRestOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var extension = GetOrCreateExtension(optionsBuilder, baseUrl);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        postgRestOptionsAction?.Invoke(new PostgRestDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the PostgREST provider, targeting the
    /// specified <paramref name="baseUrl"/>.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="Microsoft.EntityFrameworkCore.DbContext"/> type being configured.</typeparam>
    /// <param name="optionsBuilder">The typed builder being used to configure the context.</param>
    /// <param name="baseUrl">The PostgREST instance base URL.</param>
    /// <param name="postgRestOptionsAction">
    /// An optional action to further configure PostgREST-specific options.
    /// </param>
    /// <returns>The same <see cref="DbContextOptionsBuilder{TContext}"/> so that additional configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UsePostgRest<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string baseUrl,
        Action<PostgRestDbContextOptionsBuilder>? postgRestOptionsAction = null)
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UsePostgRest(baseUrl, postgRestOptionsAction);
        return optionsBuilder;
    }

    private static PostgRestDbContextOptionsExtension GetOrCreateExtension(
        DbContextOptionsBuilder optionsBuilder,
        string baseUrl)
    {
        return optionsBuilder.Options.FindExtension<PostgRestDbContextOptionsExtension>()
               ?? new PostgRestDbContextOptionsExtension(baseUrl);
    }
}
