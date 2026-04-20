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
            var annotation = prop.FindAnnotation("Relational:ColumnName");
            var columnName = annotation?.Value is string cn && !string.IsNullOrWhiteSpace(cn)
                ? cn
                : prop.Name.ToLowerInvariant();

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
