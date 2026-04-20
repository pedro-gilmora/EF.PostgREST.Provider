using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>persona</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 100, nullable).
/// </summary>

[Table("persona")]
public class Persona
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [MaxLength(100)]
    [Column("nombre")]
    public string? Nombre { get; set; }
}

