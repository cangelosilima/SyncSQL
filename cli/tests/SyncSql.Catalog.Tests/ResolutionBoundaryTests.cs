using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public sealed class ResolutionBoundaryTests
{
    private static CatalogNode Node(string server, string database, string? schema, string name, string type = "Tables", string ddl = "") => new()
    {
        Id = $"{server}/{database}/{type}/{schema}/{name}",
        Server = server,
        Database = database,
        Schema = schema,
        Name = name,
        Type = type,
        Path = $"{server}/{database}/{schema}/{type}/{name}.sql",
        Ddl = ddl,
        Engine = DatabaseEngine.MsSql,
        QualifiedName = $"{schema}.{name}",
        SizeBytes = 0,
    };

    [Fact]
    public void Resolution_BlankReferencesAndBrokerDefaultSchema()
    {
        var caller = Node("SQL", "db", null, "caller");
        var queue = Node("SQL", "db", "dbo", "queue", "Queues");
        var index = new NodeIndex([caller, queue]);
        Assert.Equal(ReferenceResolutionKind.NotFound, index.Resolve(caller, new ObjectRef(null, " ")).Kind);
        Assert.Equal(queue.Id, index.Resolve(caller, new ObjectRef(null, "queue") { ObjectType = "Queues" }).NodeId);
        Assert.Equal(ReferenceResolutionKind.NotFound, index.Resolve(caller, new ObjectRef(null, "DEFAULT") { ObjectType = "Services" }).Kind);
        Assert.False(SystemObjectCatalog.IsSystemObject(DatabaseEngine.MsSql, new ObjectRef(null, "")));
        Assert.True(SystemObjectCatalog.IsSystemObject(DatabaseEngine.MsSql, new ObjectRef(null, "custom") { Database = "master" }));
        Assert.False(SystemObjectCatalog.IsSystemObject(DatabaseEngine.MsSql, new ObjectRef("dbo", "custom") { Database = "master" }));
    }

    [Fact]
    public void Resolution_UnknownDestinationAndAmbiguousBareNames()
    {
        var caller = Node("SQL", "db", "dbo", "caller");
        var one = Node("SQL", "db2", "dbo", "t");
        var two = Node("SQL", "db3", "dbo", "t");
        var index = new NodeIndex([caller, one, two]);
        Assert.Equal(ReferenceResolutionKind.Ambiguous, index.Resolve(caller, new ObjectRef(null, "t")).Kind);
        Assert.Equal(ReferenceResolutionKind.External, new NodeIndex([]).Resolve(caller, new ObjectRef("dbo", "t")).Kind);
    }

    [Fact]
    public void Resolution_DoesNotFollowPrivateLinksFromAnotherOwner()
    {
        var caller = Node("ORA", "db", "APP", "caller");
        var target = Node("REMOTE", "db", "APP", "t");
        var link = Node("ORA", "db", "OTHER", "link", "DatabaseLinks", "CREATE DATABASE LINK link USING 'REMOTE';");
        CatalogNode[] nodes = [caller, target, link];
        var map = LinkedServerMap.FromNodes(nodes);
        Assert.Equal("ORA", map.Resolve("ORA", "link")!.OnServer);
        Assert.Equal("REMOTE", map.Resolve("ORA", "link")!.DataSource);
        Assert.Null(map.Resolve("another", "link"));
        Assert.Null(map.LinkTo("ORA", "REMOTE", "db", "APP"));
        Assert.Equal(ReferenceResolutionKind.NotFound, new NodeIndex(nodes, map).Resolve(caller, new ObjectRef("APP", "t")).Kind);
    }

    [Fact]
    public void Resolution_QualifiedLinkDoesNotWidenToAnotherHop()
    {
        var caller = Node("SQL", "db", "dbo", "caller");
        var target = Node("REMOTE", "db", "dbo", "t");
        var link = Node("SQL", "_ServerLevel", null, "link", "LinkedServers", "EXEC sp_addlinkedserver @datasrc = N'REMOTE';");
        CatalogNode[] nodes = [caller, target, link];
        var map = LinkedServerMap.FromNodes(nodes);
        var index = new NodeIndex(nodes, map);
        Assert.Equal(ReferenceResolutionKind.NotFound, index.Resolve(caller, new ObjectRef("dbo", "absent") { Server = "link" }).Kind);
        Assert.Equal(target.Id, index.Resolve(caller, new ObjectRef(null, "t") { Server = "link" }).NodeId);
        var unknown = Node("SQL", "_ServerLevel", null, "unknown", "LinkedServers");
        Assert.Null(LinkedServerMap.FromNodes([caller, unknown]).Resolve("SQL", "unknown")!.TargetServer);
        var fqdn = link with { Name = "REMOTE.example.com" };
        Assert.Equal("REMOTE", LinkedServerMap.FromNodes([caller, target, fqdn]).Resolve("SQL", fqdn.Name)!.TargetServer);
    }

    [Fact]
    public void Resolution_OneReachableServerWithSeveralMatchesIsAmbiguous()
    {
        var caller = Node("SQL", "db", "dbo", "caller");
        var link = Node("SQL", "_ServerLevel", null, "REMOTE", "LinkedServers");
        CatalogNode[] nodes = [caller, link, Node("REMOTE", "db1", "dbo", "t"), Node("REMOTE", "db2", "dbo", "t")];
        var map = LinkedServerMap.FromNodes(nodes);
        Assert.Equal(ReferenceResolutionKind.Ambiguous, new NodeIndex(nodes, map).Resolve(caller, new ObjectRef("dbo", "t")).Kind);
    }
}
