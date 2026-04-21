using Microsoft.EntityFrameworkCore;

using PosgREST.DbContext.Provider.Console;

using Xunit;

namespace PostgREST.DbContext.Provider.Tests.Tests;

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

        var productos = await ctx.Producto.Select(p => new ProductDto 
            { 
                Id = p.Id, 
                Name = p.Nombre, 
                Purchases = p.Compras
                    .Select(c => 
                        new PurchaseDto 
                        { 
                            Id = c.Id, 
                            Quantity = c.Cantidad, 
                            UnitMeasureId = c.IdUnidadMedida, 
                            Date = c.Fecha, 
                            Price = c.Precio, 
                            Currency = c.Moneda, 
                            SalePrice = c.PrecioVenta,
                            SaleCurrency = c.MonedaVenta
                        })
                    .ToList()
            }).ToListAsync();

        Assert.True(productos.Count > 0);
    }
}

internal class PurchaseDto
{
    public int Id { get; set; }
    public decimal Quantity { get; set; }
    public int UnitMeasureId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; }
    public decimal SalePrice { get; set; }
    public string SaleCurrency { get; set; }
}

internal class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<PurchaseDto> Purchases { get; internal set; }
}