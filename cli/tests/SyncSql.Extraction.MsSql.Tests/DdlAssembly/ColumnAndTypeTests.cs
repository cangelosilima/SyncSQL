using SyncSql.Extraction.MsSql.DdlAssembly;
using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public sealed class ColumnAndTypeTests
{
    [Theory]
    [InlineData("varchar", 10, "VARCHAR(10)")]
    [InlineData("char", -1, "CHAR(MAX)")]
    [InlineData("varbinary", 10, "VARBINARY(10)")]
    [InlineData("binary", 10, "BINARY(10)")]
    [InlineData("nvarchar", -1, "NVARCHAR(MAX)")]
    [InlineData("nchar", 20, "NCHAR(10)")]
    [InlineData("decimal", 0, "DECIMAL(18,2)")]
    [InlineData("numeric", 0, "NUMERIC(18,2)")]
    [InlineData("datetime2", 0, "DATETIME2(2)")]
    [InlineData("datetimeoffset", 0, "DATETIMEOFFSET(2)")]
    [InlineData("time", 0, "TIME(2)")]
    [InlineData("float", 0, "FLOAT(18)")]
    [InlineData("int", 0, "INT")]
    public void DataType_PreservesSizeAndPrecision(string name, int size, string expected) =>
        Assert.Equal(expected, ColumnDdlBuilder.DataType(name, size, 18, 2));

    [Theory]
    [InlineData(false, false, "[c] AS 1")]
    [InlineData(true, false, "[c] AS 1 PERSISTED NOT NULL")]
    [InlineData(true, true, "[c] AS 1 PERSISTED")]
    public void ComputedColumn_PreservesPersistenceAndNullability(bool persisted, bool nullable, string expected) =>
        Assert.Equal(expected, ColumnDdlBuilder.Build(new ColumnDefinitionRow { ColumnName = "c", ComputedDefinition = "1", IsPersisted = persisted, IsNullable = nullable }));

    [Theory]
    [InlineData(true, "DOCUMENT")]
    [InlineData(false, "CONTENT")]
    public void XmlColumn_PreservesCollectionAndColumnSet(bool document, string kind)
    {
        var column = new ColumnDefinitionRow { ColumnName = "c", TypeName = "xml", XmlCollectionName = "collection", XmlCollectionSchema = "dbo", IsXmlDocument = document };
        Assert.Equal($"[c] XML({kind} [dbo].[collection]) NOT NULL", ColumnDdlBuilder.Build(column));
        column.IsColumnSet = true;
        Assert.EndsWith("COLUMN_SET FOR ALL_SPARSE_COLUMNS", ColumnDdlBuilder.Build(column), StringComparison.Ordinal);
    }

    [Fact]
    public void Column_PreservesStorageIdentityAndDefaults()
    {
        var column = new ColumnDefinitionRow
        {
            ColumnName = "c",
            TypeName = "int",
            IsFileStream = true,
            IsSparse = true,
            IsRowGuid = true,
            IdentitySeed = "2",
            IdentityIncrement = "3",
            IdentityNotForReplication = true,
            DefaultDefinition = "(1)",
        };
        Assert.Equal("[c] INT FILESTREAM SPARSE IDENTITY(2,3) NOT FOR REPLICATION ROWGUIDCOL NOT NULL DEFAULT (1)", ColumnDdlBuilder.Build(column));
        column.IdentityNotForReplication = false;
        Assert.DoesNotContain("NOT FOR REPLICATION", ColumnDdlBuilder.Build(column), StringComparison.Ordinal);
        column.DefaultName = "default";
        Assert.DoesNotContain("CONSTRAINT", ColumnDdlBuilder.Build(column, tableType: true), StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [default]", ColumnDdlBuilder.Build(column), StringComparison.Ordinal);
        column.IsUserDefined = true;
        column.TypeSchema = "custom";
        column.CollationName = "Latin1_General_CI_AS";
        Assert.DoesNotContain("COLLATE", ColumnDdlBuilder.Build(column), StringComparison.Ordinal);
    }

    [Fact]
    public void Type_PreservesMemoryOptimizationDefaultAndRuleBindings()
    {
        var type = new TypeRow { SchemaName = "dbo", TypeName = "t", IsTableType = true, IsMemoryOptimized = true, DefaultName = "dbo.def", RuleName = "dbo.rule" };
        string ddl = TypeDdlBuilder.Build(type, []);
        Assert.Contains("WITH (MEMORY_OPTIMIZED = ON)", ddl, StringComparison.Ordinal);
        Assert.Contains("sp_bindefault", ddl, StringComparison.Ordinal);
        Assert.Contains("sp_bindrule", ddl, StringComparison.Ordinal);
        type.IsTableType = false;
        type.IsNullable = true;
        type.BaseTypeName = "int";
        Assert.Contains("FROM INT NULL;", TypeDdlBuilder.Build(type, []), StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedIndexAndEncryptedModule_ReturnExplanatoryComments()
    {
        Assert.StartsWith("-- Index", IndexDdlBuilder.Build(new IndexRow { TypeDesc = "XML" }), StringComparison.Ordinal);
        Assert.Contains("Definition unavailable", ModuleDdlBuilder.Build(new ModuleObjectRow()), StringComparison.Ordinal);
        Assert.DoesNotContain("DISABLE TRIGGER", ModuleDdlBuilder.Build(new ModuleObjectRow { TypeCode = "TR", Definition = "CREATE TRIGGER t ON dbo.t AFTER INSERT AS SELECT 1;" }), StringComparison.Ordinal);
        Assert.Contains("SET ANSI_NULLS ON;", ModuleDdlBuilder.Build(new ModuleObjectRow { Definition = "SELECT 1;", UsesAnsiNulls = true, UsesQuotedIdentifier = false }), StringComparison.Ordinal);
        Assert.Contains("SET QUOTED_IDENTIFIER ON;", ModuleDdlBuilder.Build(new ModuleObjectRow { Definition = "SELECT 1;", UsesAnsiNulls = false, UsesQuotedIdentifier = true }), StringComparison.Ordinal);
        Assert.Contains("@useself = N'TRUE'", LinkedServerDdlBuilder.Build("remote", null, null, null, null, null, [("user", true)]), StringComparison.Ordinal);
    }
}
