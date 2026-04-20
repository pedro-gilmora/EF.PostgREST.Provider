using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace PosgREST.DbContext.Provider.Core;

/// <summary>
/// Shared helpers for resolving PostgREST endpoint and column names from
/// EF Core metadata. Respects <c>[Table]</c> / <c>ToTable()</c> and
/// <c>[Column]</c> / <c>HasColumnName()</c> configuration via standard
/// relational annotations, falling back to the CLR name in lowercase.
/// </summary>
internal static class PostgRestNamesResolver
{

    /// <summary>
    /// Returns the PostgREST table name for the given entity type.
    /// </summary>
    extension(IEntityType entityType)
    {
        public string TableName
        {
            get
            {
                return entityType.FindAnnotation("Relational:TableName")?.Value?.ToString()
                    ?? entityType.ClrType.GetCustomAttribute<TableAttribute>()?.Name
                    ?? entityType.ClrType.Name;
            }
        }
    }

    /// <summary>
    /// Returns the PostgREST column name for the given property.
    /// </summary>

    extension(IProperty property)
    {
        public string ColumnName
        {
            get
            {
                return property.FindAnnotation("Relational:ColumnName")?.Value?.ToString()
                    ?? property.PropertyInfo?.GetCustomAttribute<ColumnAttribute>()?.Name
                    ?? property.Name;

            }
        }
    }


}
