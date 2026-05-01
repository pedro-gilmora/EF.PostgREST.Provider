using Microsoft.EntityFrameworkCore;

using PosgREST.DbContext.Provider.Console;

using PostgREST.DbContext.Provider.Analyzers.Tests.Models;

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

        var productos = await ctx.Producto.Select(p => 
            new ProductDto 
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
    [Fact]
    public async Task CascadeInsert()
    {
        using var ctx = new AppDbContext(BaseUrl);

        var prod = new Producto
        {
            Nombre = $"Producto-{DateTime.Now.Ticks}"
        };

        var unidadMedida = await ctx.UnidadMedida.FirstAsync();

        prod.Compras.Add(
            new Compra
            {
                Cantidad = 5,
                IdUnidadMedida = unidadMedida.Id,
                Fecha = DateOnly.FromDateTime(DateTime.Now),
                Precio = 15,
                Moneda = "CUP",
                PrecioVenta = 25,
                MonedaVenta = "CUP",
                Producto = prod
            });

        var productos = await ctx.Producto.AddAsync(prod);

        await ctx.SaveChangesAsync();

        Assert.Equal(EntityState.Unchanged, productos.State);

        var insertedId = prod.Id;
        var prodDto = await ctx.Producto.Select(p => new ProductDto
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
        }).FirstAsync(i => i.Id == insertedId);

        Assert.Equal(insertedId, prodDto.Id);
        Assert.Single(prodDto.Purchases);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  BATCH UPDATE / DELETE  (in-clause via Contains)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts two products, then batch-updates their names using
    /// <c>ExecuteUpdateAsync</c> with a <c>Contains</c> (in-clause) filter,
    /// and verifies the new names are persisted.
    /// </summary>
    [Fact]
    public async Task BatchUpdate_WithInClause_UpdatesMatchingRows()
    {
        using var ctx = new AppDbContext(BaseUrl);

        // Arrange – insert two products
        var suffix = DateTime.Now.Ticks;
        var p1 = new Producto { Nombre = $"BatchUpd-A-{suffix}" };
        var p2 = new Producto { Nombre = $"BatchUpd-B-{suffix}" };
        ctx.Producto.Add(p1);
        ctx.Producto.Add(p2);
        await ctx.SaveChangesAsync();

        var ids = new[] { p1.Id, p2.Id };
        var newName = $"BatchUpd-Updated-{suffix}";

        // Act – batch update via ExecuteUpdateAsync with Contains filter
        var affected = await ctx.Producto
            .Where(p => ids.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Nombre, newName));

        Assert.Equal(2, affected);

        // Assert – verify names were changed in the database
        var updated = await ctx.Producto
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        Assert.All(updated, p => Assert.Equal(newName, p.Nombre));

        // Cleanup
        await ctx.Producto.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Inserts two products, then batch-deletes them using
    /// <c>ExecuteDeleteAsync</c> with a <c>Contains</c> (in-clause) filter,
    /// and verifies they are no longer present.
    /// </summary>
    [Fact]
    public async Task BatchDelete_WithInClause_DeletesMatchingRows()
    {
        using var ctx = new AppDbContext(BaseUrl);

        // Arrange – insert two products
        var suffix = DateTime.Now.Ticks;
        var p1 = new Producto { Nombre = $"BatchDel-A-{suffix}" };
        var p2 = new Producto { Nombre = $"BatchDel-B-{suffix}" };
        ctx.Producto.Add(p1);
        ctx.Producto.Add(p2);
        await ctx.SaveChangesAsync();

        var ids = new[] { p1.Id, p2.Id };

        // Act – batch delete via ExecuteDeleteAsync with Contains filter
        var affected = await ctx.Producto
            .Where(p => ids.Contains(p.Id))
            .ExecuteDeleteAsync();

        Assert.Equal(2, affected);

        // Assert – verify rows are gone
        var remaining = await ctx.Producto
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        Assert.Empty(remaining);
    }
    [Fact]
    public async Task LoadIncludes()
    {
        using var ctx = new AppDbContext(BaseUrl);

        var productos = await ctx.Producto.Include(p => p.Compras).Include(p => p.Ventas).ToListAsync();
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
    public List<PurchaseDto> Purchases { get; internal set; } = [];
}