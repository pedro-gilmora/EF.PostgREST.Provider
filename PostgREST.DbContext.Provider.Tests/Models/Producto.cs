namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>producto</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 250).
/// </summary>
public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
