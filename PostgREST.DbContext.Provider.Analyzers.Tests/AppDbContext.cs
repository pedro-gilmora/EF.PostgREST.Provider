using Microsoft.EntityFrameworkCore;
using PosgREST.DbContext.Provider.Core;
using PosgREST.DbContext.Provider.Core.Extensions;

using PostgREST.DbContext.Provider.Analyzers.Tests.Models;

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

    public DbSet<Producto> Producto { get; set; } = null!;

    public DbSet<Venta> Venta { get; set; } = null!;
    public DbSet<Compra> Compra { get; set; } = null!;
}