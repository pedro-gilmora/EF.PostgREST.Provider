using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using PosgREST.DbContext.Provider.Core.Update;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Core database implementation for the PostgREST provider.
/// Handles <c>SaveChanges</c> by translating <see cref="IUpdateEntry"/> items
/// into PostgREST HTTP requests (POST / PATCH / DELETE).
/// Query compilation is delegated to the base class which uses the registered
/// query pipeline factories.
/// </summary>
public sealed class PostgRestDatabase : Database
{
    private readonly PostgRestUpdatePipeline _updatePipeline;

    /// <summary>
    /// Creates a new <see cref="PostgRestDatabase"/> instance.
    /// </summary>
    public PostgRestDatabase(
        DatabaseDependencies dependencies,
        PostgRestDatabaseDependencies postgRestDependencies)
        : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(postgRestDependencies);

        _updatePipeline = new PostgRestUpdatePipeline(
            postgRestDependencies.HttpClient,
            postgRestDependencies.Options);
    }

    /// <inheritdoc />
    public override int SaveChanges(IList<IUpdateEntry> entries)
        => _updatePipeline.Execute(entries);

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        IList<IUpdateEntry> entries,
        CancellationToken cancellationToken = default)
        => _updatePipeline.ExecuteAsync(entries, cancellationToken);
}
