using System.Collections.Generic;

namespace PostgREST.DbContext.Provider.Analyzers.Schema;

/// <summary>
/// Represents a single table/view definition extracted from the PostgREST
/// OpenAPI <c>definitions</c> section.
/// </summary>
internal sealed class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ColumnDefinition> Columns { get; } = new List<ColumnDefinition>();
    public HashSet<string> RequiredColumns { get; } = new HashSet<string>();
}
