using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;

namespace PosgREST.DbContext.Provider.Core.Storage;

/// <summary>
/// A <see cref="CoreTypeMapping"/> for the PostgREST provider.
/// Maps a single CLR type to its PostgREST/JSON representation using
/// built-in EF Core <see cref="JsonValueReaderWriter"/> instances.
/// </summary>
public sealed class PostgRestTypeMapping : CoreTypeMapping
{
    /// <summary>
    /// Creates a new mapping for the specified CLR type.
    /// </summary>
    public PostgRestTypeMapping(Type clrType, JsonValueReaderWriter? jsonValueReaderWriter = null)
        : base(new CoreTypeMappingParameters(
            clrType,
            converter: null,
            comparer: null,
            keyComparer: null,
            providerValueComparer: null,
            valueGeneratorFactory: GetValueGeneratorFactory(clrType),
            elementMapping: null,
            jsonValueReaderWriter: jsonValueReaderWriter))
    {
    }

    private PostgRestTypeMapping(CoreTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters)
        => new PostgRestTypeMapping(parameters);

    /// <inheritdoc />
    public override CoreTypeMapping WithComposedConverter(
        ValueConverter? converter,
        ValueComparer? comparer,
        ValueComparer? keyComparer,
        CoreTypeMapping? elementMapping,
        JsonValueReaderWriter? jsonValueReaderWriter)
    {
        return new PostgRestTypeMapping(new CoreTypeMappingParameters(
            converter?.ProviderClrType ?? ClrType,
            converter,
            comparer,
            keyComparer,
            providerValueComparer: null,
            valueGeneratorFactory: null,
            elementMapping,
            jsonValueReaderWriter ?? JsonValueReaderWriter));
    }

    /// <summary>
    /// Returns a temporary value generator factory for integer/GUID types
    /// so that <c>ValueGeneratedOnAdd</c> properties get a client-side temp value
    /// before <c>SaveChanges</c> sends the request to PostgREST.
    /// </summary>
    private static Func<IProperty, ITypeBase, ValueGenerator>? GetValueGeneratorFactory(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (underlying == typeof(int))
            return (_, _) => new TemporaryIntValueGenerator();
        if (underlying == typeof(long))
            return (_, _) => new TemporaryLongValueGenerator();
        if (underlying == typeof(short))
            return (_, _) => new TemporaryShortValueGenerator();
        if (underlying == typeof(Guid))
            return (_, _) => new GuidValueGenerator();

        return null;
    }
}
