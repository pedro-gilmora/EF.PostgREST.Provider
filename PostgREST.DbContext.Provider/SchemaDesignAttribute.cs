namespace PosgREST.DbContext.Provider.Core;

/// <summary>
/// Marks a <see cref="Microsoft.EntityFrameworkCore.DbContext"/> class for PostgREST
/// schema code generation. When applied with a valid PostgREST base URL, a Roslyn
/// code fix can fetch the OpenAPI schema and generate C# entity classes.
/// </summary>
/// <example>
/// <code>
/// [SchemaDesign("http://localhost:3000")]
/// public class AppDbContext : DbContext { }
/// </code>
/// </example>
/// <param name="url">The PostgREST instance base URL (e.g., <c>http://localhost:3000</c>).</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SchemaDesignAttribute(string url) : Attribute
{
    /// <summary>
    /// The PostgREST instance base URL used for schema code generation.
    /// </summary>
    public string Url { get; } = url;
}
