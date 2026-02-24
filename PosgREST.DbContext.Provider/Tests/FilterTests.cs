using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PosgREST.DbContext.Provider.Console.Tests;

/// <summary>
/// Integration tests for the PostgREST filter translation layer:
/// existence checks, string predicates, OR / NOT / NULL / IN filters.
/// </summary>
[Trait("Category", "Integration")]
public class FilterTests
{
    private const string BaseUrl = "http://localhost:3000";

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 8 — Existence check via FirstOrDefault
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstOrDefault_AnyCategorias_ReturnsSomething()
    {
        await using var db = new AppDbContext(BaseUrl);

        var first = await db.Categorias.FirstOrDefaultAsync();

        // The table is expected to have at least one row for these tests to be
        // meaningful. Adjust if you are testing against an empty DB.
        Assert.NotNull(first);
    }

    [Fact]
    public async Task FirstOrDefault_NonExistentId_ReturnsNull()
    {
        await using var db = new AppDbContext(BaseUrl);

        var result = await db.Categorias.FirstOrDefaultAsync(c => c.Id == -999);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 9 — StartsWith / EndsWith
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_StartsWith_OnlyMatchingRowsReturned()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Categorias
            .Where(c => c.Nombre.StartsWith("Test"))
            .ToListAsync();

        Assert.All(results, c =>
            Assert.StartsWith("Test", c.Nombre, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Where_EndsWith_OnlyMatchingRowsReturned()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Categorias
            .Where(c => c.Nombre.EndsWith("02"))
            .ToListAsync();

        Assert.All(results, c =>
            Assert.EndsWith("02", c.Nombre, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 10 — OR predicate  → ?or=(id=eq.2,id=eq.3)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_OrPredicate_ReturnsOnlyMatchingIds()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Categorias
            .Where(c => c.Id == 2 || c.Id == 3)
            .ToListAsync();

        Assert.All(results, c =>
            Assert.True(c.Id == 2 || c.Id == 3,
                $"Unexpected id {c.Id} returned by OR filter"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 11 — NOT predicate  → ?nombre=not.like.*Test*
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_NotContains_ExcludesMatchingRows()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Categorias
            .Where(c => !c.Nombre.Contains("Test"))
            .ToListAsync();

        Assert.All(results, c =>
            Assert.DoesNotContain("Test", c.Nombre, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 12 — NULL / NOT NULL check  (uses Personas which has nullable Nombre)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_NullCheck_IsNull_ReturnsOnlyNullNombre()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Personas
            .Where(p => p.Nombre == null)
            .ToListAsync();

        Assert.All(results, p =>
            Assert.Null(p.Nombre));
    }

    [Fact]
    public async Task Where_NullCheck_IsNotNull_ReturnsOnlyNonNullNombre()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Personas
            .Where(p => p.Nombre != null)
            .ToListAsync();

        Assert.All(results, p =>
            Assert.NotNull(p.Nombre));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 13 — IN filter  → ?id=in.(2,3)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_InFilter_ListContains_ReturnsOnlyMatchingIds()
    {
        await using var db = new AppDbContext(BaseUrl);
        var ids = new List<int> { 2, 3 };

        var results = await db.Categorias
            .Where(c => ids.Contains(c.Id))
            .ToListAsync();

        Assert.All(results, c =>
            Assert.Contains(c.Id, ids));
    }
}
