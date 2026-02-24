using PosgREST.DbContext.Provider.Console;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace PostgREST.DbContext.Provider.PackageTesting
{
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
}
