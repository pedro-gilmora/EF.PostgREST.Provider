using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PosgREST.DbContext.Provider.Console.Tests;

/// <summary>
/// Integration tests for <c>.Select()</c> projection translation.
/// Covers anonymous types, scalars, value tuples, custom record DTOs,
/// class member-init, nested anonymous shapes, and combined Where + Select.
/// </summary>
[Trait("Category", "Integration")]
public class SelectProjectionTests
{
    private const string BaseUrl = "http://localhost:3000";

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 14 — Anonymous-type projection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_AnonymousType_ReturnsCorrectShape()
    {
        await using var db = new AppDbContext(BaseUrl);

        var rows = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(3)
            .Select(c => new { c.Id, c.Nombre })
            .ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.True(r.Id > 0);
            Assert.NotNull(r.Nombre);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 15 — Scalar projection (single column)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_ScalarId_ReturnsPositiveIntegers()
    {
        await using var db = new AppDbContext(BaseUrl);

        var ids = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(5)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.True(id > 0));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 16 — ValueTuple projection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_ValueTuple_ReturnsCorrectPairs()
    {
        await using var db = new AppDbContext(BaseUrl);

        var tuples = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(3)
            .Select(c => ValueTuple.Create(c.Id, c.Nombre))
            .ToListAsync();

        Assert.NotEmpty(tuples);
        Assert.All(tuples, t =>
        {
            Assert.True(t.Item1 > 0);
            Assert.NotNull(t.Item2);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 17 — Custom DTO (record primary constructor)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_CustomDtoRecord_PopulatesAllProperties()
    {
        await using var db = new AppDbContext(BaseUrl);

        var dtos = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(3)
            .Select(c => new CategoriaDto(c.Id, c.Nombre))
            .ToListAsync();

        Assert.NotEmpty(dtos);
        Assert.All(dtos, d =>
        {
            Assert.True(d.Id > 0);
            Assert.NotNull(d.Nombre);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 18 — Custom class (member-init / object initializer)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_CustomClassMemberInit_PopulatesAllProperties()
    {
        await using var db = new AppDbContext(BaseUrl);

        var summaries = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(3)
            .Select(c => new CategoriaSummary { Id = c.Id, Label = c.Nombre })
            .ToListAsync();

        Assert.NotEmpty(summaries);
        Assert.All(summaries, s =>
        {
            Assert.True(s.Id > 0);
            Assert.NotNull(s.Label);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 19 — Nested anonymous types
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_NestedAnonymousTypes_PopulatesOuterAndInnerShape()
    {
        await using var db = new AppDbContext(BaseUrl);

        var rows = await db.Categorias
            .OrderBy(c => c.Id)
            .Take(3)
            .Select(c => new
            {
                c.Id,
                Info = new { c.Nombre, NameLength = c.Nombre.Length }
            })
            .ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.True(r.Id > 0);
            Assert.NotNull(r.Info.Nombre);
            Assert.Equal(r.Info.Nombre.Length, r.Info.NameLength);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TEST 20 — Where + Select combined
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_WhereAndSelectCombined_OnlyMatchingRowsWithProjection()
    {
        await using var db = new AppDbContext(BaseUrl);

        var results = await db.Categorias
            .Where(c => c.Id > 1)
            .OrderBy(c => c.Id)
            .Take(4)
            .Select(c => new { c.Id, Upper = c.Nombre.ToUpper() })
            .ToListAsync();

        Assert.All(results, r =>
        {
            Assert.True(r.Id > 1);
            Assert.Equal(r.Upper, r.Upper.ToUpperInvariant());
        });
    }
}

// ── DTO types supporting the projection tests ─────────────────────────────────

/// <summary>Custom DTO for TEST 17 — primary-constructor record projection.</summary>
file record CategoriaDto(int Id, string Nombre);

/// <summary>Custom DTO for TEST 18 — class member-init projection.</summary>
file sealed class CategoriaSummary
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}
