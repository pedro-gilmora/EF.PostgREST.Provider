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
    private const string PetstoreBaseUrl = "https://petstore.swagger.io/v2/";

    [Fact]
    public async Task FullCrudFlow_Works()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(PetstoreBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var petId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var initialName = $"integration-pet-{petId}";
        var updatedName = $"integration-pet-updated-{petId}";

        var createPayload = new PetstorePetRequest
        {
            Id = petId,
            Name = initialName,
            Status = "available",
            PhotoUrls = ["https://example.com/pet.jpg"],
            Category = new PetstoreCategory { Id = 1, Name = "integration" },
            Tags = [new PetstoreTag { Id = 1, Name = "integration" }]
        };

        var createResponse = await httpClient.PostAsJsonAsync("pet", createPayload);
        Assert.True(createResponse.IsSuccessStatusCode, await ReadErrorBodyAsync(createResponse));

        var createdPet = await createResponse.Content.ReadFromJsonAsync<PetstorePetResponse>();
        Assert.NotNull(createdPet);
        Assert.Equal(petId, createdPet!.Id);
        Assert.Equal(initialName, createdPet.Name);

        var getResponse = await httpClient.GetAsync($"pet/{petId}");
        Assert.True(getResponse.IsSuccessStatusCode, await ReadErrorBodyAsync(getResponse));

        var fetchedPet = await getResponse.Content.ReadFromJsonAsync<PetstorePetResponse>();
        Assert.NotNull(fetchedPet);
        Assert.Equal(initialName, fetchedPet!.Name);

        createPayload.Name = updatedName;
        createPayload.Status = "sold";

        var updateResponse = await httpClient.PutAsJsonAsync("pet", createPayload);
        Assert.True(updateResponse.IsSuccessStatusCode, await ReadErrorBodyAsync(updateResponse));

        var getUpdatedResponse = await httpClient.GetAsync($"pet/{petId}");
        Assert.True(getUpdatedResponse.IsSuccessStatusCode, await ReadErrorBodyAsync(getUpdatedResponse));

        var updatedPet = await getUpdatedResponse.Content.ReadFromJsonAsync<PetstorePetResponse>();
        Assert.NotNull(updatedPet);
        Assert.Equal(updatedName, updatedPet!.Name);
        Assert.Equal("sold", updatedPet.Status);

        var deleteResponse = await httpClient.DeleteAsync($"pet/{petId}");
        Assert.True(deleteResponse.IsSuccessStatusCode, await ReadErrorBodyAsync(deleteResponse));

        var getAfterDeleteResponse = await httpClient.GetAsync($"pet/{petId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"Status: {(int)response.StatusCode} {response.StatusCode}; Body: {body}";
    }

    private class PetstorePetRequest
    {
        public long Id { get; set; }
        public PetstoreCategory Category { get; set; } = new();
        public string Name { get; set; } = string.Empty;
        public List<string> PhotoUrls { get; set; } = [];
        public List<PetstoreTag> Tags { get; set; } = [];
        public string Status { get; set; } = string.Empty;
    }

    private class PetstorePetResponse
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private class PetstoreCategory
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class PetstoreTag
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
