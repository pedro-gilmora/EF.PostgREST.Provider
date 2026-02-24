namespace PostgREST.DbContext.Provider.Analyzers.Schema;

/// <summary>
/// Represents a single column/property extracted from a PostgREST table definition.
/// </summary>
internal sealed class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>JSON Schema type (<c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>).</summary>
    public string JsonType { get; set; } = "string";

    /// <summary>PostgreSQL format hint (<c>uuid</c>, <c>timestamp with time zone</c>, etc.).</summary>
    public string? Format { get; set; }

    /// <summary>Description from the OpenAPI spec; may contain <c>&lt;pk/&gt;</c> for primary keys.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the column is a primary key (detected from <c>&lt;pk/&gt;</c> marker).</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>Optional default expression (e.g., <c>now()</c>).</summary>
    public string? Default { get; set; }

    /// <summary>Optional max-length constraint.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Whether the column is an array type.</summary>
    public bool IsArray { get; set; }

    /// <summary>Enum values when the column is an enum type.</summary>
    public string[]? EnumValues { get; set; }
}
