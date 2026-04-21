using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PosgREST.DbContext.Provider.Console.Tests;

/// <summary>
/// Integration test for full CRUD flow against Swagger Petstore API.
/// </summary>
[Trait("Category", "Integration")]
public class PetStoreTest
{
    private const string _baseUrl = "https://petstore.swagger.io/v2/";

    [Fact]
    public async Task FullCrudFlow_Works()
    {
        using var db = new PetDbContext(_baseUrl);

    }
}
