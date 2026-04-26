using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Runtime helpers for materializing PostgREST embedded-resource arrays
/// (nested JSON arrays returned inside the parent JSON object) into typed
/// DTO collections.
/// </summary>
public static class PostgRestNestedCollectionHelper
{
    /// <summary>
    /// Populates navigation collection properties on <paramref name="entity"/>
    /// from the embedded JSON arrays produced by PostgREST when
    /// <c>?select=*,nav(*)</c> is used.
    /// </summary>
    public static void PopulateIncludes<T>(T entity, JsonElement parentElement, List<ColumnsTree> includes)
    {
        if (entity is null) return;

        foreach (var include in includes)
        {
            var tableName = include.ColumnName;
            var clrNav = entity.GetType().GetProperty(include.MemberName, BindingFlags.Public | BindingFlags.Instance);

            if (clrNav is null || !clrNav.CanWrite && !typeof(IList).IsAssignableFrom(clrNav.PropertyType))
                continue;

            var targetClrType = include.TargetEntityType.ClrType;
            var properties = include.TargetEntityType.GetProperties().ToList();

            if (!parentElement.TryGetProperty(tableName, out var array)
                || array.ValueKind != JsonValueKind.Array)
                continue;

            if (include.IsCollection)
            {
                // Build a List<TTarget> and assign / populate the existing collection
                var listType = typeof(List<>).MakeGenericType(targetClrType);
                var list = (IList)Activator.CreateInstance(listType)!;
                foreach (var element in array.EnumerateArray())
                    list.Add(MaterializeEntity(element, properties, targetClrType));

                if (clrNav.CanWrite)
                    clrNav.SetValue(entity, list);
                else
                {
                    // Navigation is a readonly ICollection<T> — try to populate via Add
                    var existing = clrNav.GetValue(entity) as IList;
                    if (existing is not null)
                        foreach (var item in list)
                            existing.Add(item);
                }
            }
            else
            {
                // Single reference navigation
                if (array.GetArrayLength() > 0)
                {
                    var related = MaterializeEntity(array[0], properties, targetClrType);
                    if (clrNav.CanWrite)
                        clrNav.SetValue(entity, related);
                }
            }
        }
    }

    private static object MaterializeEntity(JsonElement element, IReadOnlyList<IProperty> properties, Type clrType)
    {
        var entity = Activator.CreateInstance(clrType)!;
        foreach (var prop in properties)
        {
            if (!element.TryGetProperty(prop.ColumnName, out var jsonProp)
                || jsonProp.ValueKind == JsonValueKind.Undefined)
                continue;

            var clrProp = clrType.GetProperty(prop.Name);
            if (clrProp is null || !clrProp.CanWrite)
                continue;

            clrProp.SetValue(entity, ConvertJsonValue(jsonProp, prop.ClrType));
        }
        return entity;
    }

    /// <summary>
    /// Reads the nested JSON array at <paramref name="propertyName"/> from
    /// <c>queryContext.CurrentJsonElement</c>, materializes each element as
    /// <typeparamref name="TEntity"/> using EF Core metadata, then projects
    /// it through <paramref name="selector"/> to produce a
    /// <see cref="List{TDto}"/>.
    /// </summary>
    public static List<TDto> Read<TEntity, TDto>(
        QueryContext queryContext,
        IEntityType entityType,
        string propertyName,
        Func<TEntity, TDto> selector)
        where TEntity : class, new()
    {
        var ctx = (PostgRestQueryContext)queryContext;
        var root = ctx.CurrentJsonElement;

        if (!root.TryGetProperty(propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array)
            return [];

        var properties = entityType.GetProperties().ToList();
        var result = new List<TDto>(array.GetArrayLength());

        foreach (var element in array.EnumerateArray())
        {
            var entity = MaterializeEntity<TEntity>(element, properties);
            result.Add(selector(entity));
        }

        return result;
    }

    /// <summary>
    /// Creates an instance of <typeparamref name="TEntity"/> by reading each
    /// mapped column from <paramref name="element"/> and assigning it via
    /// reflection (using the CLR property name from EF Core metadata).
    /// </summary>
    private static TEntity MaterializeEntity<TEntity>(JsonElement element, IReadOnlyList<IProperty> properties)
        where TEntity : class, new()
    {
        var entity = new TEntity();
        var clrType = typeof(TEntity);

        foreach (var prop in properties)
        {
            // Resolve the JSON key — respects [Column] / HasColumnName()
            var columnName = prop.ColumnName;

            if (!element.TryGetProperty(columnName, out var jsonProp)
                || jsonProp.ValueKind == JsonValueKind.Undefined)
                continue;

            var clrProp = clrType.GetProperty(prop.Name);
            if (clrProp is null || !clrProp.CanWrite)
                continue;

            var value = ConvertJsonValue(jsonProp, prop.ClrType);
            clrProp.SetValue(entity, value);
        }

        return entity;
    }

    /// <summary>Converts a <see cref="JsonElement"/> to the requested CLR type.</summary>
    public static object? ConvertJsonValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(int)) return element.GetInt32();
        if (underlying == typeof(long)) return element.GetInt64();
        if (underlying == typeof(short)) return element.GetInt16();
        if (underlying == typeof(byte)) return element.GetByte();
        if (underlying == typeof(bool)) return element.GetBoolean();
        if (underlying == typeof(string)) return element.GetString();
        if (underlying == typeof(decimal)) return element.GetDecimal();
        if (underlying == typeof(double)) return element.GetDouble();
        if (underlying == typeof(float)) return element.GetSingle();
        if (underlying == typeof(Guid)) return element.GetGuid();
        if (underlying == typeof(DateTime)) return element.GetDateTime();
        if (underlying == typeof(DateTimeOffset)) return element.GetDateTimeOffset();
        if (underlying == typeof(DateOnly) && element.GetString() is { } dateStr)
            return DateOnly.Parse(dateStr);
        if (underlying == typeof(TimeOnly) && element.GetString() is { } timeStr)
            return TimeOnly.Parse(timeStr);
        if (underlying == typeof(byte[]))
            return element.GetBytesFromBase64();
        if (underlying == typeof(JsonElement))
            return element.Clone();

        return JsonSerializer.Deserialize(element.GetRawText(), targetType);
    }
}
