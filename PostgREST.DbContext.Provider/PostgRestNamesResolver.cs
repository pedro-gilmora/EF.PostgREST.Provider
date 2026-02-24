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
                // EF Core stores the table name via the "Relational:TableName" annotation
                // when [Table("x")] or entity.ToTable("x") is used.
                var annotation = entityType.FindAnnotation("Relational:TableName");
                if (annotation?.Value is string tableName && !string.IsNullOrWhiteSpace(tableName))
                    return tableName;

                return entityType.ClrType.Name.ToLowerInvariant();
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
                // EF Core stores the column name via the "Relational:ColumnName" annotation
                // when [Column("x")] or HasColumnName("x") is used.
                var annotation = property.FindAnnotation("Relational:ColumnName");
                if (annotation?.Value is string columnName && !string.IsNullOrWhiteSpace(columnName))
                    return columnName;

                return property.Name.ToLowerInvariant();

            }
        }
    }


}
