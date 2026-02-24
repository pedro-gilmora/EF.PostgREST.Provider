using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using PosgREST.DbContext.Provider.Core.Infrastructure;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Identifies this EF Core provider as the PostgREST provider.
/// Registered via <see cref="PostgRestDbContextOptionsExtension.ApplyServices"/>.
/// </summary>
public sealed class PostgRestDatabaseProvider(DatabaseProviderDependencies dependencies)
    : DatabaseProvider<PostgRestDbContextOptionsExtension>(dependencies)
{
}
