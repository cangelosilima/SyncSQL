using SyncSql.Extraction.MsSql.DdlAssembly;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public class IndexDdlBuilderTests
{
    [Fact]
    public void Build_NonclusteredIndex_ProducesCreateIndexStatement()
    {
        string ddl = IndexDdlBuilder.Build("dbo", "Orders", "IX_Orders_CustomerId", isUnique: false, "NONCLUSTERED", "[CustomerId] ASC", includedColumns: null);

        Assert.Equal("CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId] ON [dbo].[Orders] ([CustomerId] ASC);", ddl);
    }

    [Fact]
    public void Build_UniqueIndexWithIncludedColumns_AddsUniqueAndIncludeClause()
    {
        string ddl = IndexDdlBuilder.Build("dbo", "Orders", "UX_Orders_Code", isUnique: true, "CLUSTERED", "[Code] ASC", "[Status]");

        Assert.Equal("CREATE UNIQUE CLUSTERED INDEX [UX_Orders_Code] ON [dbo].[Orders] ([Code] ASC) INCLUDE ([Status]);", ddl);
    }

    [Fact]
    public void Build_UnsupportedIndexType_ProducesInformationalComment()
    {
        string ddl = IndexDdlBuilder.Build("dbo", "Orders", "SIDX_Geo", isUnique: false, "SPATIAL", "[Location]", null);

        Assert.Equal("-- Index [SIDX_Geo] (SPATIAL) - see sys.indexes for full definition", ddl);
    }
}
