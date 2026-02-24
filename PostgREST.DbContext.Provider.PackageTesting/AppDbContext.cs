using Microsoft.EntityFrameworkCore;
using PosgREST.DbContext.Provider.Core;
using PosgREST.DbContext.Provider.Core.Extensions;

using PostgREST.DbContext.Provider.PackageTesting.Models;

namespace PosgREST.DbContext.Provider.Console;

/// <summary>
/// DbContext targeting the PostgREST instance at the configured base URL.
/// </summary>
[SchemaDesign("http://localhost:3000")]
public class AppDbContext(string baseUrl) : Microsoft.EntityFrameworkCore.DbContext
{
    //public DbSet<Producto> Productos { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UsePostgRest(baseUrl);
    }

    public DbSet<PurchaseSupplier> PurchaseSupplier { get; set; } = null!;
    public DbSet<Precio> Precio { get; set; } = null!;
    public DbSet<Categoria> Categoria { get; set; } = null!;
    public DbSet<Venta> Venta { get; set; } = null!;
    public DbSet<Persona> Persona { get; set; } = null!;
    public DbSet<Producto> Producto { get; set; } = null!;
    public DbSet<Compra> Compra { get; set; } = null!;
    public DbSet<UnidadMedida> UnidadMedida { get; set; } = null!;
    public DbSet<Logs> Logs { get; set; } = null!;
    public DbSet<TipoPago> TipoPago { get; set; } = null!;
    public DbSet<Categorizacion> Categorizacion { get; set; } = null!;
    public DbSet<Rate> Rate { get; set; } = null!;}