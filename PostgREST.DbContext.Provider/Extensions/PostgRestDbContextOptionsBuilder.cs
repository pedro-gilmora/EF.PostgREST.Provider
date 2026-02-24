using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Extensions;

/// <summary>
/// Provider-specific options builder for the PostgREST EF Core provider.
/// Exposes a fluent API to configure bearer tokens, schema targeting,
/// request timeouts, and other PostgREST-specific settings.
/// </summary>
/// <remarks>
/// Obtain an instance of this builder from the
/// <see cref="PostgRestDbContextOptionsBuilderExtensions.UsePostgRest"/> callback:
/// <code>
/// optionsBuilder.UsePostgRest("http://localhost:3000", pgrest =>
/// {
///     pgrest.WithBearerToken("my-jwt");
///     pgrest.WithSchema("api");
///     pgrest.WithTimeout(TimeSpan.FromSeconds(30));
/// });
/// </code>
/// </remarks>
public class PostgRestDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    /// <summary>
    /// Creates a new <see cref="PostgRestDbContextOptionsBuilder"/>.
    /// </summary>
    internal PostgRestDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        _optionsBuilder = optionsBuilder;
    }

    /// <summary>
    /// Sets the JWT bearer token included in the <c>Authorization</c> header
    /// of every HTTP request sent to PostgREST.
    /// </summary>
    /// <param name="token">The JWT bearer token.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PostgRestDbContextOptionsBuilder WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        UpdateExtension(e => e.WithBearerToken(token));
        return this;
    }

    /// <summary>
    /// Targets a non-default PostgreSQL schema via the
    /// <c>Accept-Profile</c> and <c>Content-Profile</c> PostgREST headers.
    /// </summary>
    /// <param name="schema">The PostgreSQL schema name.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PostgRestDbContextOptionsBuilder WithSchema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        UpdateExtension(e => e.WithSchema(schema));
        return this;
    }

    /// <summary>
    /// Sets the default HTTP request timeout for all PostgREST calls.
    /// When <c>null</c>, the <see cref="HttpClient"/> default is used.
    /// </summary>
    /// <param name="timeout">The request timeout.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PostgRestDbContextOptionsBuilder WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                "Timeout must be a positive duration.");

        UpdateExtension(e => e.WithTimeout(timeout));
        return this;
    }

    private void UpdateExtension(
        Func<PostgRestDbContextOptionsExtension, PostgRestDbContextOptionsExtension> configure)
    {
        var current = _optionsBuilder.Options
            .FindExtension<PostgRestDbContextOptionsExtension>()
            ?? throw new InvalidOperationException(
                "PostgREST extension has not been registered. Call UsePostgRest first.");

        var updated = configure(current);
        ((IDbContextOptionsBuilderInfrastructure)_optionsBuilder).AddOrUpdateExtension(updated);
    }
}
