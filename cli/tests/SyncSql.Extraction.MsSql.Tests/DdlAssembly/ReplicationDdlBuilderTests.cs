using SyncSql.Extraction.MsSql.DdlAssembly;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public class ReplicationDdlBuilderTests
{
    [Fact]
    public void Build_NoArticles_SaysNone()
    {
        string ddl = ReplicationDdlBuilder.Build("PublOrders", description: null, articlesCsv: null);

        Assert.Contains("--   (none)", ddl);
        Assert.DoesNotContain("-- Description:", ddl);
    }

    [Fact]
    public void Build_WithDescriptionAndArticles_ListsEachArticle()
    {
        string ddl = ReplicationDdlBuilder.Build("PublOrders", "Order data replication", "Orders, OrderLines");

        Assert.Contains("-- Description: Order data replication", ddl);
        Assert.Contains("--   - Orders", ddl);
        Assert.Contains("--   - OrderLines", ddl);
        Assert.Contains("Subscribers are not enumerated", ddl);
    }
}
