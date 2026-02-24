namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Represents a PostgREST ordering clause: <c>column.asc</c> or <c>column.desc</c>.
/// </summary>
/// <param name="Column">The PostgREST column name.</param>
/// <param name="Ascending"><c>true</c> for ascending, <c>false</c> for descending.</param>
public readonly record struct PostgRestOrderByClause(string Column, bool Ascending)
{
    /// <summary>
    /// Formats as a PostgREST <c>order</c> segment: <c>column.asc</c> or <c>column.desc</c>.
    /// </summary>
    public override string ToString() => $"{Column}.{(Ascending ? "asc" : "desc")}";
}
