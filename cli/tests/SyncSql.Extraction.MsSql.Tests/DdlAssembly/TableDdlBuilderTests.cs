using SyncSql.Extraction.MsSql.DdlAssembly;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public class TableDdlBuilderTests
{
    [Fact]
    public void Build_NoPrimaryKey_OmitsConstraintClause()
    {
        string ddl = TableDdlBuilder.Build("dbo", "Orders", "    [OrderId] INT NOT NULL", primaryKeyDdl: null);

        string expected = string.Join('\n',
            "CREATE TABLE [dbo].[Orders] (",
            "    [OrderId] INT NOT NULL",
            ");");
        Assert.Equal(expected, ddl);
    }

    [Fact]
    public void Build_WithPrimaryKey_AppendsCommaAndConstraint()
    {
        string ddl = TableDdlBuilder.Build(
            "dbo", "Orders",
            "    [OrderId] INT NOT NULL",
            "  CONSTRAINT [PK_Orders] PRIMARY KEY ([OrderId] ASC)");

        string expected = string.Join('\n',
            "CREATE TABLE [dbo].[Orders] (",
            "    [OrderId] INT NOT NULL,",
            "  CONSTRAINT [PK_Orders] PRIMARY KEY ([OrderId] ASC)",
            ");");
        Assert.Equal(expected, ddl);
    }

    [Fact]
    public void Build_EmptyColumnsDdl_StillProducesValidShape()
    {
        string ddl = TableDdlBuilder.Build("dbo", "Empty", string.Empty, null);

        Assert.Equal("CREATE TABLE [dbo].[Empty] (\n);", ddl);
    }
}
