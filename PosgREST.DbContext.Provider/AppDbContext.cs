using Microsoft.EntityFrameworkCore;
using PosgREST.DbContext.Provider.Console.Models;
using PosgREST.DbContext.Provider.Core.Extensions;

namespace PosgREST.DbContext.Provider.Console;

/// <summary>
/// DbContext targeting the PostgREST instance at the configured base URL.
/// Exposes the <c>categoria</c>, <c>producto</c>, and <c>persona</c> tables from the real schema.
/// </summary>
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    private readonly string _baseUrl;

    public AppDbContext(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Persona> Personas => Set<Persona>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UsePostgRest(_baseUrl);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Nombre).HasMaxLength(50);
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Nombre).HasMaxLength(250);
        });

        modelBuilder.Entity<Persona>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Nombre).HasMaxLength(100).IsRequired(false);
        });
    }
}
