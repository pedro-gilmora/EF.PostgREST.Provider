# SourceCrafter.EF.PostgREST.Provider

An **Entity Framework Core custom database provider** that targets a [PostgREST](https://postgrest.org/) HTTP API instead of a traditional database connection. Write standard LINQ queries and `SaveChanges()` calls — the provider translates them into PostgREST-compatible HTTP requests automatically.

[![NuGet](https://img.shields.io/nuget/v/SourceCrafter.EF.PostgREST.Provider.svg)](https://www.nuget.org/packages/SourceCrafter.EF.PostgREST.Provider)
[![CI](https://github.com/pedro-gilmora/EF.PostgREST.Provider/actions/workflows/deploy.yml/badge.svg)](https://github.com/pedro-gilmora/EF.PostgREST.Provider/actions/workflows/deploy.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Zero-config LINQ → PostgREST translation** — `WHERE`, `SELECT`, `ORDER BY`, `SKIP/TAKE` all map to PostgREST query-string parameters.
- **Full CRUD via `SaveChanges`** — `Added` → `POST`, `Modified` → `PATCH`, `Deleted` → `DELETE`.
- **Projection support** — anonymous types, scalars, `ValueTuple`, record DTOs, class member-init, nested shapes.
- **Filter operators** — `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `like`, `ilike`, `is null`, OR predicates, NOT predicates, `IN` lists.
- **JWT authentication** — pass a bearer token through provider options.
- **Non-default schema support** — `Accept-Profile` / `Content-Profile` headers.
- **`IHttpClientFactory` integration** — no raw `HttpClient` instances.
- **`System.Text.Json` source generators** — AOT-safe serialization.
- Targets **.NET 10** and **EF Core 10**.

---

## Installation

```shell
dotnet add package SourceCrafter.EF.PostgREST.Provider
```

---

## Quick Start

### 1. Define your entities

```csharp
public class Categoria
{
    public int    Id     { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
```

### 2. Create your `DbContext`

```csharp
using Microsoft.EntityFrameworkCore;
using PosgREST.DbContext.Provider.Core.Extensions;

public class AppDbContext : DbContext
{
    private readonly string _baseUrl;

    public AppDbContext(string baseUrl) => _baseUrl = baseUrl;

    public DbSet<Categoria> Categorias => Set<Categoria>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UsePostgRest(_baseUrl);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Categoria>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Nombre).HasMaxLength(50);
        });
    }
}
```

### 3. Query data

```csharp
await using var db = new AppDbContext("http://localhost:3000");

// SELECT all
var all = await db.Categorias.ToListAsync();

// Filtered + sorted + paged
var page = await db.Categorias
    .Where(c => c.Nombre.StartsWith("Test"))
    .OrderBy(c => c.Nombre)
    .Skip(0).Take(10)
    .ToListAsync();

// Projection
var names = await db.Categorias
    .Select(c => new { c.Id, c.Nombre })
    .ToListAsync();
```

### 4. Mutate data

```csharp
// INSERT
var cat = new Categoria { Nombre = "NewCategory" };
db.Categorias.Add(cat);
await db.SaveChangesAsync();
Console.WriteLine(cat.Id); // server-assigned PK

// UPDATE
cat.Nombre = "Updated";
await db.SaveChangesAsync();

// DELETE
db.Categorias.Remove(cat);
await db.SaveChangesAsync();
```

---

## LINQ → PostgREST Translation Reference

| LINQ / EF Core                            | PostgREST query-string              |
|-------------------------------------------|-------------------------------------|
| `.Where(e => e.Id == 1)`                  | `?id=eq.1`                          |
| `.Where(e => e.Name != "x")`              | `?name=neq.x`                       |
| `.Where(e => e.Age > 18)`                 | `?age=gt.18`                        |
| `.Where(e => e.Age >= 18)`                | `?age=gte.18`                       |
| `.Where(e => e.Age < 65)`                 | `?age=lt.65`                        |
| `.Where(e => e.Name.Contains("foo"))`     | `?name=like.*foo*`                  |
| `.Where(e => e.Name.StartsWith("A"))`     | `?name=like.A*`                     |
| `.Where(e => e.Name.EndsWith("z"))`       | `?name=like.*z`                     |
| `.Where(e => !e.Name.Contains("foo"))`    | `?name=not.like.*foo*`              |
| `.Where(e => e.Id == 2 \|\| e.Id == 3)`   | `?or=(id=eq.2,id=eq.3)`             |
| `.Where(e => e.Name == null)`             | `?name=is.null`                     |
| `.Where(e => e.Name != null)`             | `?name=not.is.null`                 |
| `.OrderBy(e => e.Name)`                   | `?order=name.asc`                   |
| `.OrderByDescending(e => e.Name)`         | `?order=name.desc`                  |
| `.Skip(10).Take(5)`                       | `?offset=10&limit=5`                |
| `.Select(e => new { e.Id, e.Name })`      | `?select=id,name`                   |

### EntityState → HTTP Verb

| EF Core `EntityState` | HTTP Verb | PostgREST Pattern                          |
|-----------------------|-----------|--------------------------------------------|
| `Added`               | `POST`    | `POST /{table}`                            |
| `Modified`            | `PATCH`   | `PATCH /{table}?{pk}=eq.{value}`           |
| `Deleted`             | `DELETE`  | `DELETE /{table}?{pk}=eq.{value}`          |
| `Unchanged` (query)   | `GET`     | `GET /{table}?select={cols}&{filters}`     |

---

## Tests

Integration tests run against a live PostgREST instance at `http://localhost:3000`. Start PostgREST pointing to your database before running:

```shell
dotnet test PosgREST.DbContext.Provider/PosgREST.DbContext.Provider.Console.csproj \
    --logger "console;verbosity=normal"
```

### Test coverage overview

| Suite | What is tested |
|---|---|
| `CrudTests` | SELECT all, SELECT with WHERE, ORDER BY + Take, full INSERT → UPDATE → DELETE lifecycle |
| `FilterTests` | `FirstOrDefault`, `StartsWith`, `EndsWith`, OR predicates, NOT predicates, NULL / NOT NULL checks, `IN` lists |
| `SelectProjectionTests` | Anonymous types, scalars, `ValueTuple`, record DTOs, class member-init, nested anonymous shapes, combined WHERE + SELECT |

---

## Project Structure

```
PosgREST.DbContext.Provider.Core/     ← EF Core provider (the NuGet package)
│   Extensions/                       ← UsePostgRest() options builder
│   Infrastructure/                   ← IDbContextOptionsExtension
│   Query/                            ← LINQ → HTTP translation pipeline
│   Storage/                          ← IDatabase, IDatabaseCreator, type mapping
│   Update/                           ← SaveChanges HTTP update pipeline
│   Diagnostics/                      ← Logging definitions
│
PosgREST.DbContext.Provider/          ← Sample app + integration tests (xUnit)
    AppDbContext.cs
    Models/
    Tests/
```

---

## Configuration Options

| Option | Default | Description |
|---|---|---|
| Base URL | *(required)* | PostgREST root URL, e.g. `http://localhost:3000` |
| Bearer token | `null` | JWT passed as `Authorization: Bearer {token}` |
| Schema | `public` | PostgreSQL schema (`Accept-Profile` / `Content-Profile`) |

---

## Requirements

| Dependency | Version |
|---|---|
| .NET | 10 |
| EF Core | 10.0.x |
| PostgREST | 12+ |

---

## Contributing

Pull requests are welcome. For significant changes, please open an issue first to discuss what you would like to change.

---

## License

[MIT](LICENSE)
