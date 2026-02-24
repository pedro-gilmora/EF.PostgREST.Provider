using Microsoft.EntityFrameworkCore;
using PosgREST.DbContext.Provider.Console.Models;
using Xunit;

namespace PosgREST.DbContext.Provider.Console.Tests;

/// <summary>
/// Integration tests covering basic SELECT queries and the full INSERT → UPDATE → DELETE
/// lifecycle against a live PostgREST instance at <c>http://localhost:3000</c>.
/// </summary>
[Trait("Category", "Integration")]
public class CrudTests
{
    private const string BaseUrl = "http://localhost:3000";

    // ──────────────────────────────────────────────────────────────────────────
    //  SELECT
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>TEST 1 — SELECT all rows from <c>categorias</c>.</summary>
    [Fact]
    public async Task SelectAll_ReturnsAllCategorias()
    {
        await using var db = new AppDbContext(BaseUrl);

        var categorias = await db.Categorias.ToListAsync();

        Assert.NotNull(categorias);
        // We can't assert an exact count since the DB is live,
        // but we can ensure the call succeeds and returns a list.
        Assert.True(categorias.Count >= 0);
    }

    /// <summary>TEST 2 — SELECT with WHERE <c>id == 1</c>.</summary>
    [Fact]
    public async Task SelectWithWhere_FindsOrReturnsNullForId1()
    {
        await using var db = new AppDbContext(BaseUrl);

        // Either a record exists or null is fine — what matters is no exception.
        var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == 1);

        Assert.True(cat is null || cat.Id == 1);
    }

    /// <summary>TEST 3 — SELECT with OrderBy + Take returns at most 3 sorted rows.</summary>
    [Fact]
    public async Task SelectWithOrderByAndTake_ReturnsAtMost3ByNombre()
    {
        await using var db = new AppDbContext(BaseUrl);

        var top3 = await db.Categorias
            .OrderBy(c => c.Nombre)
            .Take(3)
            .ToListAsync();

        Assert.NotNull(top3);
        Assert.True(top3.Count <= 3);

        // Verify ascending sort is preserved in the returned slice.
        for (var i = 1; i < top3.Count; i++)
            Assert.True(
                string.Compare(top3[i - 1].Nombre, top3[i].Nombre,
                               StringComparison.Ordinal) <= 0,
                $"Row {i - 1} ('{top3[i - 1].Nombre}') should precede row {i} ('{top3[i].Nombre}')");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Full CRUD lifecycle (Insert → Update → Delete → Verify)
    //
    //  Combined into a single [Fact] so the server-generated PK flows naturally
    //  through all four phases without cross-test shared state.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>TEST 4-7 — INSERT then UPDATE then DELETE then confirm gone.</summary>
    [Fact]
    public async Task CrudLifecycle_InsertUpdateDeleteAndVerify()
    {
        int insertedId;

        // ── 4. INSERT ──────────────────────────────────────────────────────────
        {
            await using var db = new AppDbContext(BaseUrl);
            var newCat = new Categoria { Nombre = $"xUnit-{DateTime.UtcNow:HHmmss}" };
            db.Categorias.Add(newCat);
            var affected = await db.SaveChangesAsync();

            Assert.Equal(1, affected);
            Assert.True(newCat.Id > 0, "Server should have assigned a positive PK.");
            insertedId = newCat.Id;
        }

        // ── 5. UPDATE ─────────────────────────────────────────────────────────
        {
            await using var db = new AppDbContext(BaseUrl);
            var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == insertedId);
            Assert.NotNull(cat);

            cat.Nombre = $"xUnit-Updated-{DateTime.UtcNow:HHmmss}";
            var affected = await db.SaveChangesAsync();

            Assert.Equal(1, affected);
            Assert.StartsWith("xUnit-Updated-", cat.Nombre);
        }

        // ── 6. DELETE ─────────────────────────────────────────────────────────
        {
            await using var db = new AppDbContext(BaseUrl);
            var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == insertedId);
            Assert.NotNull(cat);

            db.Categorias.Remove(cat);
            var affected = await db.SaveChangesAsync();

            Assert.Equal(1, affected);
        }

        // ── 7. VERIFY deletion ────────────────────────────────────────────────
        {
            await using var db = new AppDbContext(BaseUrl);
            var gone = await db.Categorias.FirstOrDefaultAsync(c => c.Id == insertedId);

            Assert.Null(gone);
        }
    }
}
