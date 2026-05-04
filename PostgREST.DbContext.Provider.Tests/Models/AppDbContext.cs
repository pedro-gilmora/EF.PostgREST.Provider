using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PosgREST.DbContext.Provider.Core;
using PosgREST.DbContext.Provider.Core.Extensions;

using PostgREST.DbContext.Provider.Analyzers.Tests.Models;
using PostgREST.DbContext.Provider.Tests.Models;

using System.Diagnostics;

namespace PosgREST.DbContext.Provider.Console;

/// <summary>
/// DbContext targeting the PostgREST instance at the configured base URL.
/// </summary>
[SchemaDesign("http://localhost:3000")]
public class AppDbContext(string baseUrl, bool enableLogs = false, bool enableLazyLoad = false) : Microsoft.EntityFrameworkCore.DbContext
{
    //public DbSet<Producto> Productos { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UsePostgRest(baseUrl);
        
        if(enableLazyLoad) optionsBuilder.UseLazyLoadingProxies();

#if DEBUG
        if (enableLogs)
            optionsBuilder
                .LogTo(s => Trace.WriteLine(s), LogLevel.Debug)   // prints all EF Core + PostgREST events
                .EnableSensitiveDataLogging();                     // includes request bodies / filter values
#endif

    }

    public DbSet<Producto> Producto { get; set; } = null!;

    public DbSet<Venta> Venta { get; set; } = null!;
    public DbSet<Compra> Compra { get; set; } = null!;

    public DbSet<Categoria> Categoria { get; set; } = null!;
    public DbSet<Persona> Persona { get; set; } = null!;
    public DbSet<UnidadMedida> UnidadMedida { get; set; } = null!;
}