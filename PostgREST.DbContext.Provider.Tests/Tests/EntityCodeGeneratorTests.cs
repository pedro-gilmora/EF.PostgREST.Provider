using PostgREST.DbContext.Provider.Analyzers.Schema;
using Xunit;

namespace PostgREST.DbContext.Provider.Tests;

public class EntityCodeGeneratorTests
{
    [Fact]
    public void GenerateSingle_CompositeKey_EmitsPrimaryKeyAttribute()
    {
        var table = new TableDefinition { Name = "purchaseSupplier" };
        table.Columns.Add(new ColumnDefinition { Name = "purchaseId", JsonType = "integer", IsPrimaryKey = true });
        table.Columns.Add(new ColumnDefinition { Name = "supplierId", JsonType = "integer", IsPrimaryKey = true });
        table.Columns.Add(new ColumnDefinition { Name = "purchaseDate", JsonType = "string", Format = "timestamp" });
        table.Columns.Add(new ColumnDefinition { Name = "amount", JsonType = "number", Format = "numeric" });

        var code = EntityCodeGenerator.GenerateSingle(table, "TestNamespace");

        Assert.Contains("[PrimaryKey(nameof(PurchaseId), nameof(SupplierId))]", code);
        Assert.Contains("using Microsoft.EntityFrameworkCore;", code);
        Assert.DoesNotContain("[Key]", code);
    }

    [Fact]
    public void GenerateSingle_SingleKey_EmitsKeyAttribute()
    {
        var table = new TableDefinition { Name = "producto" };
        table.Columns.Add(new ColumnDefinition { Name = "id", JsonType = "integer", IsPrimaryKey = true });
        table.Columns.Add(new ColumnDefinition { Name = "nombre", JsonType = "string" });
        table.RequiredColumns.Add("id");
        table.RequiredColumns.Add("nombre");

        var code = EntityCodeGenerator.GenerateSingle(table, "TestNamespace");

        Assert.Contains("[Key]", code);
        Assert.DoesNotContain("[PrimaryKey", code);
        Assert.DoesNotContain("using Microsoft.EntityFrameworkCore;", code);
    }

    [Fact]
    public void GenerateSingle_CompositeKey_DoesNotEmitKeyOnProperties()
    {
        var table = new TableDefinition { Name = "categorizacion" };
        table.Columns.Add(new ColumnDefinition { Name = "idProducto", JsonType = "integer", IsPrimaryKey = true });
        table.Columns.Add(new ColumnDefinition { Name = "idCategoria", JsonType = "integer", IsPrimaryKey = true });

        var code = EntityCodeGenerator.GenerateSingle(table, "TestNamespace");

        Assert.Contains("[PrimaryKey(nameof(IdProducto), nameof(IdCategoria))]", code);
        Assert.DoesNotContain("[Key]", code);
    }
}
