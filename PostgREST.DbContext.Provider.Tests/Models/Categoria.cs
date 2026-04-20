using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>categoria</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 50).
/// </summary>

[Table("categoria")]
public class Categoria
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("nombre")]
    public string Nombre { get; set; } = default!;
}
