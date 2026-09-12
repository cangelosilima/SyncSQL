using SyncSql.Core.Domain;

using SyncSql.Core.Configuration;

namespace SyncSql.Catalog.Tests;

public class LinkedServerMapTests
{
    [Fact]
    public void Private_links_are_scoped_to_the_service_and_owner()
    {
        CatalogNode procurement = Link("HELIOS", "REMOTE", "CREATE DATABASE LINK REMOTE USING 'ATLAS'", "DatabaseLinks") with
        {
            Id = "procurement-link",
            Database = "APP_PDB",
            Schema = "PROCUREMENT",
        };
        CatalogNode compliance = procurement with
        {
            Id = "compliance-link",
            Schema = "COMPLIANCE",
            Ddl = "CREATE DATABASE LINK REMOTE USING 'MERIDIAN'",
        };
        LinkedServerMap map = LinkedServerMap.FromNodes([
            procurement, compliance, Table("ATLAS", "Commerce", "ITEMS"), Table("MERIDIAN", "Distribution", "ITEMS")]);
        Assert.Equal("ATLAS", map.Resolve("HELIOS", "REMOTE", "APP_PDB", "PROCUREMENT")?.TargetServer);
        Assert.Equal("MERIDIAN", map.Resolve("HELIOS", "REMOTE.WORLD", "APP_PDB", "COMPLIANCE")?.TargetServer);
        Assert.Null(map.Resolve("HELIOS", "REMOTE"));
        Assert.Null(map.Resolve("HELIOS", "REMOTE", "OTHER_PDB", "PROCUREMENT"));
        Assert.Null(map.Resolve("HELIOS", "REMOTE", "APP_PDB", "REPORTING"));
        Assert.Null(map.LinkTo("HELIOS", "MERIDIAN", "APP_PDB", "PROCUREMENT"));
    }

    private static CatalogNode Link(string server, string name, string ddl, string type = "LinkedServers") => new()
    {
        Id = $"{server}/_ServerLevel/{type}/{name}",
        Server = server,
        Database = "_ServerLevel",
        Type = type,
        Name = name,
        QualifiedName = name,
        Path = "irrelevant",
        Ddl = ddl,
        SizeBytes = 0,
    };

    private static CatalogNode Table(string server, string database, string name) => new()
    {
        Id = $"{server}/{database}/Tables/dbo/{name}",
        Server = server,
        Database = database,
        Schema = "dbo",
        Type = "Tables",
        Name = name,
        QualifiedName = $"dbo.{name}",
        Path = "irrelevant",
        Ddl = "irrelevant",
        SizeBytes = 0,
    };

    private static string MsSqlLinkDdl(string name, string dataSource, string catalog = "") => $"""
        EXEC sp_addlinkedserver
            @server = N'{name}',
            @srvproduct = N'SQL Server',
            @provider = N'SQLNCLI',
            @datasrc = N'{dataSource}',
            @provstr = N'',
            @catalog = N'{catalog}';
        GO
        """;

    [Fact]
    public void FromNodes_DoesNotSubstituteLinkNameForAnUnknownDataSource()
    {
        CatalogNode remote = Table("SQLPROD02", "SalesDb", "Orders");
        CatalogNode link = Link("SQLPROD01", "SQLPROD02", MsSqlLinkDdl("SQLPROD02", "10.0.0.7"));

        LinkedServerMap map = LinkedServerMap.FromNodes([remote, link]);

        Assert.Null(map.Resolve("SQLPROD01", "SQLPROD02")?.TargetServer);
        Assert.Empty(map.ReachableFrom("SQLPROD01"));
    }

    [Fact]
    public void FromNodes_MatchesACatalogServerByTheDataSourceHost()
    {
        CatalogNode remote = Table("SQLPROD02", "SalesDb", "Orders") with { ServerIdentity = Identity("sqlprod02.corp.example.com") };
        CatalogNode link = Link("SQLPROD01", "SALES_LINK", MsSqlLinkDdl("SALES_LINK", "sqlprod02.corp.example.com,1433", "SalesDb"));

        LinkedServerMap map = LinkedServerMap.FromNodes([remote, link]);
        LinkedServerLink? resolved = map.Resolve("SQLPROD01", "SALES_LINK");

        Assert.Equal("SQLPROD02", resolved?.TargetServer);
        Assert.Equal("SalesDb", resolved?.DefaultDatabase);
    }

    [Fact]
    public void FromNodes_KeepsALinkNothingAnswersTo_ButWithoutATargetServer()
    {
        CatalogNode link = Link("SQLPROD01", "THIRD_PARTY", MsSqlLinkDdl("THIRD_PARTY", "vendor-host.example.net"));

        LinkedServerMap map = LinkedServerMap.FromNodes([link]);
        LinkedServerLink? resolved = map.Resolve("SQLPROD01", "THIRD_PARTY");

        Assert.NotNull(resolved);
        Assert.Null(resolved?.TargetServer);
        Assert.Empty(map.ReachableFrom("SQLPROD01"));
    }

    [Fact]
    public void FromNodes_LoopbackLink_ResolvesToItsOwnServerButAddsNoReachability()
    {
        CatalogNode local = Table("SQLPROD01", "AppDb", "Orders");
        CatalogNode link = Link("SQLPROD01", "SELFLINK", MsSqlLinkDdl("SELFLINK", "SQLPROD01"));

        LinkedServerMap map = LinkedServerMap.FromNodes([local, link]);

        Assert.Equal("SQLPROD01", map.Resolve("SQLPROD01", "SELFLINK")?.TargetServer);
        Assert.Empty(map.ReachableFrom("SQLPROD01"));
    }

    [Fact]
    public void Resolve_MatchesAnOracleDatabaseLinkWrittenWithItsDomain()
    {
        CatalogNode remote = Table("ORAPROD02", "ORCLPDB1", "ORDERS") with
        {
            ServerIdentity = ServerIdentity.FromConfig(new ServerConfig
            {
                Name = "ORAPROD02",
                Host = "oracle.example.com",
                Type = DatabaseEngine.Oracle,
                ServiceName = "ORCLPDB1",
                CredentialsVariablePrefix = "ORA",
                Aliases = ["oraprod02_tns"],
            }),
        };
        CatalogNode link = Link(
            "ORAPROD01",
            "ORAPROD02",
            "CREATE DATABASE LINK \"ORAPROD02\" CONNECT TO \"APP\" IDENTIFIED BY VALUES ':1' USING 'oraprod02_tns'",
            type: "DatabaseLinks");

        LinkedServerMap map = LinkedServerMap.FromNodes([remote, link]);

        Assert.Equal("ORAPROD02", map.Resolve("ORAPROD01", "ORAPROD02.WORLD")?.TargetServer);
    }

    [Fact]
    public void LinkTo_FindsTheLinkThatReachesAServer()
    {
        CatalogNode remote = Table("SQLPROD02", "SalesDb", "Orders");
        CatalogNode link = Link("SQLPROD01", "SALES_LINK", MsSqlLinkDdl("SALES_LINK", "SQLPROD02"));

        LinkedServerMap map = LinkedServerMap.FromNodes([remote, link]);

        Assert.Equal(link.Id, map.LinkTo("SQLPROD01", "SQLPROD02")?.NodeId);
        Assert.Null(map.LinkTo("SQLPROD02", "SQLPROD01"));
    }

    private static ServerIdentity Identity(string host, params string[] aliases) => ServerIdentity.FromConfig(new ServerConfig
    {
        Name = host,
        Host = host,
        Type = DatabaseEngine.MsSql,
        CredentialsVariablePrefix = "TEST",
        Aliases = aliases,
    });

    [Fact]
    public void MultipleOwnersAndAliasesConvergeOnOneDestination()
    {
        CatalogNode remote = Table("CENTRAL", "SalesDb", "Orders") with { ServerIdentity = Identity("central.example.com", "10.0.0.5") };
        CatalogNode first = Link("ONE", "REPORTING", MsSqlLinkDdl("REPORTING", "central.example.com"));
        CatalogNode second = Link("TWO", "SALES", MsSqlLinkDdl("SALES", "10.0.0.5"));
        LinkedServerMap map = LinkedServerMap.FromNodes([first, second, remote]);
        Assert.Equal("CENTRAL", map.Resolve("ONE", "REPORTING")?.TargetServer);
        Assert.Equal("CENTRAL", map.Resolve("TWO", "SALES")?.TargetServer);
        Assert.NotEqual(map.Resolve("ONE", "REPORTING")?.NodeId, map.Resolve("TWO", "SALES")?.NodeId);
    }

    [Theory]
    [InlineData("host.other.com")]
    [InlineData(@"host\OTHER")]
    [InlineData("host,1444")]
    public void DoesNotDiscardAddressQualifiers(string dataSource)
    {
        var remote = Table("host", "db", "t");
        var link = Link("ROOT", "host", MsSqlLinkDdl("host", dataSource));
        Assert.Null(LinkedServerMap.FromNodes([remote, link]).Resolve("ROOT", "host")?.TargetServer);
    }

    [Fact]
    public void DeclaredDestinationWinsOverCollidingLocalAlias()
    {
        var wrong = Table("REPORTING", "db", "t");
        var right = Table("CENTRAL", "db", "t") with { ServerIdentity = Identity("central.example.com") };
        var link = Link("ROOT", "REPORTING", MsSqlLinkDdl("REPORTING", "central.example.com"));
        Assert.Equal("CENTRAL", LinkedServerMap.FromNodes([wrong, right, link]).Resolve("ROOT", "REPORTING")?.TargetServer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingAliasNeverPicksFirst(bool reverse)
    {
        CatalogNode[] nodes = [Table("ONE", "db", "t") with { ServerIdentity = Identity("one", "shared") },
            Table("TWO", "db", "t") with { ServerIdentity = Identity("two", "shared") },
            Link("ROOT", "LINK", MsSqlLinkDdl("LINK", "shared"))];
        Assert.Null(LinkedServerMap.FromNodes(reverse ? nodes.Reverse() : nodes).Resolve("ROOT", "LINK")?.TargetServer);
    }

    [Fact]
    public void MultipleRoutesRequireAnExplicitLink()
    {
        var first = Link("ROOT", "ONE", MsSqlLinkDdl("ONE", "CENTRAL"));
        var second = Link("ROOT", "TWO", MsSqlLinkDdl("TWO", "CENTRAL"));
        var map = LinkedServerMap.FromNodes([first, second, Table("CENTRAL", "db", "t")]);
        Assert.Single(map.ReachableFrom("ROOT"));
        Assert.Null(map.LinkTo("ROOT", "CENTRAL"));
        Assert.Equal("CENTRAL", map.Resolve("ROOT", "ONE")?.TargetServer);
        Assert.Equal("CENTRAL", map.Resolve("ROOT", "TWO")?.TargetServer);
    }

    [Theory]
    [InlineData("shared", true)]
    [InlineData("unknown", false)]
    public void NestedExportsCannotOverrideAmbiguousOrContradictoryMetadata(string address, bool addCompetitor)
    {
        var link = Link("ROOT", "LINK", MsSqlLinkDdl("LINK", address)) with { Path = "ROOT/LinkedServers/LINK.sql" };
        var nested = Table("ONE", "db", "t") with { Path = "ROOT/LinkedServers/LINK/db/dbo/Tables/t.sql", ServerIdentity = Identity("one", "shared") };
        List<CatalogNode> nodes = [link, nested];
        if (addCompetitor)
        {
            nodes.Add(Table("TWO", "db", "t") with { ServerIdentity = Identity("two", "shared") });
        }
        Assert.Null(LinkedServerMap.FromNodes(nodes).Resolve("ROOT", "LINK")?.TargetServer);
    }
}
