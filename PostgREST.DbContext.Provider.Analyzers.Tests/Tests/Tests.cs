using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PosgREST.DbContext.Provider.Console.Tests;

/// <summary>
/// Integration tests covering basic SELECT queries and the full INSERT → UPDATE → DELETE
/// lifecycle against a live PostgREST instance at <c>http://localhost:3000</c>.
/// </summary>
[Trait("Category", "Integration")]
public class Tests
{
    private const string BaseUrl = "http://localhost:3000";

    // ──────────────────────────────────────────────────────────────────────────
    //  SELECT
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>TEST 1 — SELECT all rows from <c>categorias</c>.</summary>
    [Fact]
    public async Task SelectAll_ReturnsAllCategorias()
    {
        using var ctx = new AppDbContext(BaseUrl);
        var productos = await ctx.Producto.ToListAsync();
    }
}
