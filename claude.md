# PosgREST.DbContext.Provider

## Project Purpose

Entity Framework Core custom database provider that targets a **PostgREST** HTTP API instead of a traditional database connection. The provider translates EF Core operations (LINQ queries, `SaveChanges`) into PostgREST-compatible HTTP requests against a live PostgREST instance.

A **code generator** reads the PostgREST OpenAPI schema from a configurable URL (default: `http://localhost:3000/`) and emits strongly-typed `DbContext`, entity classes, and configuration.

---

## Solution Structure

| Project | Role |
|---|---|
| `PosgREST.DbContext.Provider.Core` | EF Core provider implementation (class library, .NET 10) |
| `PosgREST.DbContext.Provider.Console` | Sample / CLI host for schema discovery and code generation (.NET 10) |

---

## Technology Stack

- **Runtime**: .NET 10, C# 14
- **EF Core**: Microsoft.EntityFrameworkCore (latest stable/preview compatible with .NET 10)
- **HTTP**: `System.Net.Http.HttpClient` / `IHttpClientFactory`
- **Schema source**: PostgREST OpenAPI 3.x endpoint (`GET /` on the PostgREST instance)
- **Serialization**: `System.Text.Json`

---

## Architecture — EF Core Provider Components

The following EF Core extension points must be implemented in the Core project. Reference: [Writing a Database Provider – EF Core (MS Learn)](https://learn.microsoft.com/ef/core/providers/writing-a-provider).

### 1. Options & Service Registration

| Type to implement | Purpose |
|---|---|
| `PostgRestDbContextOptionsExtension` : `IDbContextOptionsExtension` | Registers provider services in the DI container; carries configuration (base URL, auth token, etc.) |
| `PostgRestOptionsExtension` (public API helper) | Fluent `.UsePostgRest(url)` method on `DbContextOptionsBuilder` |
| `PostgRestDbContextOptionsBuilder` | Provider-specific options surface (timeout, default schema, etc.) |

### 2. Database & Connection

| Type | Purpose |
|---|---|
| `PostgRestDatabaseProvider` : `IDatabaseProvider` | Returns `true` from `IsConfigured`; identifies this provider |
| `PostgRestDatabase` : `IDatabase` | Entry point called by EF Core for `SaveChanges`; delegates to the HTTP update pipeline |
| `PostgRestDatabaseCreator` : `IDatabaseCreator` | No-op or throws — PostgREST manages the schema; `EnsureCreated` is not applicable |

### 3. Query Pipeline

| Type | Purpose |
|---|---|
| `PostgRestQueryCompiler` : `IQueryCompiler` | Compiles expression trees into an executable that issues `GET` requests with PostgREST query-string filters |
| `PostgRestQueryableMethodTranslatingExpressionVisitor` | Translates `.Where()`, `.Select()`, `.OrderBy()`, `.Skip()`, `.Take()` into PostgREST query parameters (`?select=`, `?order=`, `?offset=`, `?limit=`, horizontal/vertical filtering) |
| `PostgRestQuerySqlGeneratorFactory` | Produces the final HTTP request URL + query string from the translated expression |
| `PostgRestShapedQueryCompilingExpressionVisitor` | Materializes `HttpResponseMessage` JSON payloads into entity instances |

### 4. Update Pipeline (SaveChanges)

| Type | Purpose |
|---|---|
| `PostgRestModificationCommandBatch` | Groups tracked-entity changes into HTTP requests per table |
| `PostgRestUpdateSqlGenerator` | Builds the HTTP request (URL, verb, body) for each modification command |

### 5. Type Mapping

| Type | Purpose |
|---|---|
| `PostgRestTypeMappingSource` : `ITypeMappingSource` | Maps CLR types ↔ PostgREST/PostgreSQL JSON types returned by the schema |

### 6. Model Validation

| Type | Purpose |
|---|---|
| `PostgRestModelValidator` : `IModelValidator` | Validates that the model is compatible with PostgREST constraints (e.g., no client-side sequences, no complex inheritance) |

---

## EntityState → HTTP Verb Mapping

EF Core `EntityState` values must translate to PostgREST HTTP operations as follows:

| EntityState | HTTP Verb | PostgREST Endpoint Pattern | Request Body |
|---|---|---|---|
| `Added` | **POST** | `POST /{table}` | JSON object (or array for bulk) |
| `Modified` | **PATCH** | `PATCH /{table}?{pk_column}=eq.{value}` | JSON object with changed columns only |
| `Deleted` | **DELETE** | `DELETE /{table}?{pk_column}=eq.{value}` | *(none)* |
| `Unchanged` (query) | **GET** | `GET /{table}?select={columns}&{filters}` | *(none)* |

### PostgREST Query-String Filter Syntax (subset to support)

| EF Core / LINQ | PostgREST query-string |
|---|---|
| `.Where(e => e.Id == 1)` | `?id=eq.1` |
| `.Where(e => e.Name != "x")` | `?name=neq.x` |
| `.Where(e => e.Age > 18)` | `?age=gt.18` |
| `.Where(e => e.Age >= 18)` | `?age=gte.18` |
| `.Where(e => e.Age < 65)` | `?age=lt.65` |
| `.Where(e => e.Name.Contains("foo"))` | `?name=like.*foo*` |
| `.OrderBy(e => e.Name)` | `?order=name.asc` |
| `.OrderByDescending(e => e.Name)` | `?order=name.desc` |
| `.Skip(10).Take(5)` | `?offset=10&limit=5` |
| `.Select(e => new { e.Id, e.Name })` | `?select=id,name` |

### PostgREST Headers

| Header | When |
|---|---|
| `Content-Type: application/json` | POST, PATCH |
| `Accept: application/json` | All requests |
| `Prefer: return=representation` | POST, PATCH, DELETE — to get the affected rows back for EF Core change tracker |
| `Prefer: count=exact` | When `.Count()` or `.LongCount()` is used |
| `Authorization: Bearer {token}` | When JWT auth is configured |
| `Accept-Profile: {schema}` / `Content-Profile: {schema}` | When targeting a non-default PostgreSQL schema |

---

## Schema Discovery & Code Generation

### OpenAPI Schema Endpoint

PostgREST exposes an **OpenAPI 3.x** document at its root (`GET /`). The generator must:

1. Fetch the schema JSON from the configured URL.
2. Parse `paths` — each key (e.g., `"/todos"`) maps to a table/view and becomes an entity class.
3. Parse `definitions` (or `components/schemas`) — each definition's `properties` map to entity properties with CLR types derived from `type` + `format`.
4. Detect primary keys from `required` fields and `x-pk` / unique constraints exposed in the schema.
5. Detect foreign-key relationships from PostgREST's embedding hints to generate navigation properties.

### Generated Artifacts

| Artifact | Description |
|---|---|
| Entity POCO classes | One class per table/view; properties match columns |
| `PostgRestDbContext` subclass | `DbSet<T>` per entity; `OnModelCreating` configures keys, relationships |
| `IEntityTypeConfiguration<T>` classes | Fluent configuration per entity |

### Type Mapping (PostgREST/JSON → CLR)

| PostgREST `type` + `format` | CLR Type |
|---|---|
| `integer` / `int4` | `int` |
| `bigint` / `int8` | `long` |
| `smallint` / `int2` | `short` |
| `boolean` | `bool` |
| `text`, `character varying` | `string` |
| `numeric`, `decimal` | `decimal` |
| `real` / `float4` | `float` |
| `double precision` / `float8` | `double` |
| `uuid` | `Guid` |
| `date` | `DateOnly` |
| `timestamp without time zone` | `DateTime` |
| `timestamp with time zone` | `DateTimeOffset` |
| `time without time zone` | `TimeOnly` |
| `jsonb`, `json` | `JsonElement` or `JsonDocument` |
| `bytea` | `byte[]` |
| arrays (e.g., `text[]`) | `List<T>` |

---

## Coding Standards

- **C# 14** language features are allowed (field keyword, extension members, etc.).
- **Nullable reference types** are enabled — never suppress with `!` without justification.
- Use **file-scoped namespaces**.
- Use **primary constructors** where appropriate.
- Prefer `readonly record struct` for small immutable value types.
- All public API types must have XML doc comments.
- Follow the **namespace convention**: `PosgREST.DbContext.Provider.Core.*` for the provider, `PosgREST.DbContext.Provider.Console` for the host.
- Async all the way: every I/O operation (HTTP calls) must be `async Task` / `async ValueTask`.
- Use `IHttpClientFactory` — never instantiate raw `HttpClient`.
- Use `System.Text.Json` source generators (`JsonSerializerContext`) for AOT compatibility.
- **No EF Core InMemory provider dependency** — this *is* the provider.

---

## Key Reference Links

- [Writing an EF Core Database Provider](https://learn.microsoft.com/ef/core/providers/writing-a-provider)
- [EF Core Provider Model](https://learn.microsoft.com/ef/core/providers/)
- [EF Core SaveChanges Internals](https://learn.microsoft.com/ef/core/saving/)
- [PostgREST API Reference](https://postgrest.org/en/stable/references/api.html)
- [PostgREST Tables & Views](https://postgrest.org/en/stable/references/api/tables_views.html)
- [PostgREST Schema Structure](https://postgrest.org/en/stable/references/api/schemas.html)
