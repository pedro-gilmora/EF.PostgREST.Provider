using Microsoft.EntityFrameworkCore;

using PosgREST.DbContext.Provider.Core;
using PosgREST.DbContext.Provider.Core.Extensions;

namespace PosgREST.DbContext.Provider.Console;

/// <summary>
/// DbContext dedicated to Swagger Petstore schema generation and integration tests.
/// </summary>
[SchemaDesign("https://petstore.swagger.io/v2/swagger.json")]
public sealed class PetDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    private readonly string _baseUrl;

    public PetDbContext(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UsePostgRest(_baseUrl);
    }
}
