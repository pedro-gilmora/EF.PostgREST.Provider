namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>persona</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 100, nullable).
/// </summary>
public class Persona
{
    public int Id { get; set; }
    public string? Nombre { get; set; }
}
