using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public class CatalogServerIdentityTests
{
    [Fact]
    public void MergeObjects_PreservesLegacyPathsAndPrefersDirectSourcePathsAcrossRoots()
    {
        CatalogNode direct = Node("SERVER") with { Path = "MSSQL/SERVER/App/Tables/dbo/Orders.sql", SourcePath = "SERVER/App/Tables/dbo/Orders.sql" };
        CatalogNode nested = direct with { Path = "a/ROOT/LinkedServers/SERVER/App/Tables/dbo/Orders.sql", SourcePath = "ROOT/LinkedServers/SERVER/App/Tables/dbo/Orders.sql" };
        Assert.Same(direct, Assert.Single(CatalogServerIdentity.MergeObjects([nested, direct])));
        CatalogNode legacy = Node("LEGACY");
        Assert.Same(legacy, Assert.Single(CatalogServerIdentity.MergeObjects([legacy])));
    }

    [Fact]
    public void Canonicalize_AddsConfiguredNameToTheServerNamesAndLeavesLegacyNodesAlone()
    {
        CatalogNode identified = Node("REMOTE_ALIAS") with
        {
            ServerIdentity = SyncSql.Core.Configuration.ServerIdentity.FromConfig(new SyncSql.Core.Configuration.ServerConfig
            {
                Name = "REMOTE",
                Host = "remote.example.com",
                Type = DatabaseEngine.MsSql,
                CredentialsVariablePrefix = "REMOTE",
            }),
        };
        CatalogNode missingName = Node("MISSING_NAME") with
        {
            ServerIdentity = new SyncSql.Core.Configuration.ServerIdentity
            {
                Engine = DatabaseEngine.MsSql,
                Endpoint = "missing.example.com,1433",
            },
        };
        CatalogNode legacy = Node("LEGACY");

        List<CatalogNode> result = CatalogServerIdentity.Canonicalize([identified, missingName, legacy]);

        CatalogNode actual = Assert.Single(result, node => node.Server == "REMOTE_ALIAS");
        Assert.Contains("REMOTE", actual.ServerNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("REMOTE", actual.ActualServerName);
        Assert.Null(Assert.Single(result, node => node.Server == "MISSING_NAME").ActualServerName);
        Assert.Same(legacy, Assert.Single(result, node => node.Server == "LEGACY"));
    }

    private static CatalogNode Node(string server) => new()
    {
        Id = $"{server}/App/Tables/dbo/Orders",
        Server = server,
        Database = "App",
        Schema = "dbo",
        Type = "Tables",
        Name = "Orders",
        QualifiedName = "dbo.Orders",
        Path = $"{server}/App/Tables/dbo/Orders.sql",
        Ddl = "CREATE TABLE dbo.Orders (Id int);",
        SizeBytes = 1,
    };
}
