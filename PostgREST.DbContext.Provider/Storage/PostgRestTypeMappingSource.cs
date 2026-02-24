using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// Type mapping source for the PostgREST provider.
/// Maps CLR types to <see cref="PostgRestTypeMapping"/> instances
/// with the appropriate <see cref="JsonValueReaderWriter"/>.
/// </summary>
/// <remarks>
/// PostgREST speaks JSON exclusively, so every type is mapped through
/// EF Core's built-in JSON reader/writer infrastructure. This covers the
/// types listed in the <c>claude.md</c> type-mapping table.
/// </remarks>
public class PostgRestTypeMappingSource : TypeMappingSource
{
    private static readonly ConcurrentDictionary<Type, PostgRestTypeMapping?> _mappingCache = new();

    /// <summary>
    /// Mapping from CLR type to the EF Core <see cref="JsonValueReaderWriter"/>
    /// singleton that reads/writes that type in JSON.
    /// </summary>
    private static readonly Dictionary<Type, JsonValueReaderWriter> _jsonReaderWriters = new()
    {
        [typeof(bool)] = JsonBoolReaderWriter.Instance,
        [typeof(byte)] = JsonByteReaderWriter.Instance,
        [typeof(short)] = JsonInt16ReaderWriter.Instance,
        [typeof(int)] = JsonInt32ReaderWriter.Instance,
        [typeof(long)] = JsonInt64ReaderWriter.Instance,
        [typeof(float)] = JsonFloatReaderWriter.Instance,
        [typeof(double)] = JsonDoubleReaderWriter.Instance,
        [typeof(decimal)] = JsonDecimalReaderWriter.Instance,
        [typeof(string)] = JsonStringReaderWriter.Instance,
        [typeof(char)] = JsonCharReaderWriter.Instance,
        [typeof(Guid)] = JsonGuidReaderWriter.Instance,
        [typeof(DateTime)] = JsonDateTimeReaderWriter.Instance,
        [typeof(DateTimeOffset)] = JsonDateTimeOffsetReaderWriter.Instance,
        [typeof(DateOnly)] = JsonDateOnlyReaderWriter.Instance,
        [typeof(TimeOnly)] = JsonTimeOnlyReaderWriter.Instance,
        [typeof(TimeSpan)] = JsonTimeSpanReaderWriter.Instance,
        [typeof(byte[])] = JsonByteArrayReaderWriter.Instance,
    };

    /// <summary>
    /// Set of CLR types the PostgREST provider can map natively.
    /// </summary>
    private static readonly HashSet<Type> _supportedPrimitives =
    [
        typeof(bool),
        typeof(byte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(char),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(byte[]),
        typeof(JsonElement),
    ];

    /// <summary>
    /// Creates a new <see cref="PostgRestTypeMappingSource"/>.
    /// </summary>
    public PostgRestTypeMappingSource(TypeMappingSourceDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override CoreTypeMapping? FindMapping(Type type)
    {
        return FindMappingForType(type);
    }

    /// <inheritdoc />
    public override CoreTypeMapping? FindMapping(IProperty property)
    {
        return FindMappingForType(property.ClrType);
    }

    /// <inheritdoc />
    public override CoreTypeMapping? FindMapping(MemberInfo member)
    {
        var clrType = member switch
        {
            PropertyInfo pi => pi.PropertyType,
            FieldInfo fi => fi.FieldType,
            _ => null
        };

        return clrType is not null ? FindMappingForType(clrType) : null;
    }

    private static PostgRestTypeMapping? FindMappingForType(Type type)
    {
        return _mappingCache.GetOrAdd(type, static t =>
        {
            var underlying = Nullable.GetUnderlyingType(t);
            var effectiveType = underlying ?? t;

            if (_jsonReaderWriters.TryGetValue(effectiveType, out var readerWriter))
            {
                return new PostgRestTypeMapping(t, readerWriter);
            }

            if (effectiveType == typeof(JsonElement))
            {
                return new PostgRestTypeMapping(t);
            }

            if (effectiveType.IsEnum)
            {
                return new PostgRestTypeMapping(t);
            }

            return null;
        });
    }

    /// <inheritdoc />
    protected override CoreTypeMapping? FindCollectionMapping(
        TypeMappingInfo info,
        Type modelType,
        Type? providerType,
        CoreTypeMapping? elementMapping)
    {
        // PostgREST handles collections as JSON arrays at the HTTP level.
        // No special collection type mapping is needed in the provider.
        return null;
    }
}
