using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Metadata;
#pragma warning disable CS0618

var tms = new TestTMS();
var prop = typeof(Dummy).GetProperty("Id")!;

Console.WriteLine("--- Calling FindMapping(MemberInfo) ---");
var r1 = tms.FindMapping(prop);
Console.WriteLine("Result: " + (r1 != null ? "HIT" : "null"));

Console.WriteLine("--- Calling FindMapping(MemberInfo, null, true) ---");
var r2 = tms.FindMapping(prop, null!, true);
Console.WriteLine("Result: " + (r2 != null ? "HIT" : "null"));

Console.WriteLine("--- Calling FindMapping(Type) ---");
var r3 = tms.FindMapping(typeof(int));
Console.WriteLine("Result: " + (r3 != null ? "HIT" : "null"));

class Dummy { public int Id { get; set; } }

class TestTMS : TypeMappingSource
{
    public TestTMS() : base(new TypeMappingSourceDependencies(
        null!, null!, Array.Empty<ITypeMappingSourcePlugin>())) { }
    public override CoreTypeMapping? FindMapping(MemberInfo member)
    {
        Console.WriteLine("  >> FindMapping(MemberInfo) OVERRIDE called");
        return new TestMapping(typeof(int));
    }
    public override CoreTypeMapping? FindMapping(Type type)
    {
        Console.WriteLine("  >> FindMapping(Type) OVERRIDE called for " + type.Name);
        return new TestMapping(type);
    }
}

class TestMapping : CoreTypeMapping
{
    public TestMapping(Type clrType) : base(new CoreTypeMappingParameters(
        clrType, null, null, null, null, null, null, JsonInt32ReaderWriter.Instance)) { }
    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters) => new TestMapping(parameters.ClrType);
    public override CoreTypeMapping WithComposedConverter(
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter? c,
        Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer? cmp,
        Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer? kcmp,
        CoreTypeMapping? elem,
        JsonValueReaderWriter? jrw) => this;
}
