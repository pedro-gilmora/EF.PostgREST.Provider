namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>categoria</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 50).
/// </summary>
public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
