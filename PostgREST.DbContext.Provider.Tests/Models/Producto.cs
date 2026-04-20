using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosgREST.DbContext.Provider.Console.Models;

/// <summary>
/// Entity representing a row in the PostgREST <c>producto</c> table.
/// Schema: PK <c>id</c> (integer), <c>nombre</c> (varchar 250).
/// </summary>
[Table("producto")]
public class Producto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(250)]
    [Column("nombre")]
    public string Nombre { get; set; } = default!;
}
