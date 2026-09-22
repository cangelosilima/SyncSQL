using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public sealed class CrossEngineLinkTests
{
    private static CatalogNode Node(string server, string database, string? schema, string name, string type = "Tables", DatabaseEngine engine = DatabaseEngine.MsSql) => new()
    {
        Id = $"{server}/{database}/{type}/{schema}/{name}",
        Server = server,
        Database = database,
        Schema = schema,
        Name = name,
        QualifiedName = name,
        Type = type,
        Engine = engine,
        Ddl = "",
        Path = $"{engine}/{server}/{database}/{type}/{schema}/{name}.sql",
        SizeBytes = 0,
        ServerIdentity = ServerIdentity.FromConfig(new() { Name = server, Type = engine, Host = server + ".example.com", ServiceName = engine == DatabaseEngine.Oracle ? database : null }),
    };

    private static CatalogNode Caller => Node("ORA", "PDB", "APP", "REPORT", "Views", DatabaseEngine.Oracle);
    private static CatalogNode Link => Node("ORA", "PDB", "APP", "DL_ORDER", "DatabaseLinks", DatabaseEngine.Oracle) with
    {
        Link = new() { ConnectIdentifier = "GATEWAY_DEVELOPMENT", DataSource = "SQL.example.com,1433", TargetEngine = DatabaseEngine.MsSql, Logins = [new("entitlements")] },
    };

    [Fact]
    public void Independently_extracted_login_and_database_user_resolve_the_remote_default_context()
    {
        CatalogNode login = Node("SQL", "_ServerLevel", null, "entitlements", "Logins") with { Principal = new() { Login = "entitlements", DefaultDatabase = "Orders" } };
        CatalogNode user = Node("SQL", "Orders", null, "different_user_name", "Users") with { Principal = new() { Login = "entitlements", DefaultSchema = "sales" } };
        CatalogNode table = Node("SQL", "Orders", "sales", "Orders");
        CatalogNode[] nodes = [Caller, Link, login, user, table, Node("SQL", "Orders", "dbo", "Orders"), Node("SQL", "Other", "sales", "Orders")];
        var map = LinkedServerMap.FromNodes(nodes);
        ReferenceResolution resolved = new NodeIndex(nodes, map).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" });
        Assert.Equal(table.Id, resolved.NodeId);
        Assert.Equal(Link.Id, resolved.ViaLink?.NodeId);
        LinkMetadata metadata = map.MetadataFor(Link.Id)!;
        Assert.Equal("SQL", metadata.TargetServer);
        Assert.Equal("Orders", metadata.Database);
        Assert.Equal("sales", metadata.DefaultSchema);
        Assert.Contains(login.Id, metadata.LoginNodeIds);
        Assert.Contains(user.Id, metadata.LoginNodeIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unresolved_and_ambiguous_remote_objects_keep_the_link_pointer(bool ambiguous)
    {
        CatalogNode[] nodes = [Caller, Link, Node("SQL", "Orders", "sales", ambiguous ? "Orders" : "Other"), Node("SQL", "Orders", "dbo", "Orders")];
        var index = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes));
        ReferenceResolution resolution = index.Resolve(Caller, new(null, ambiguous ? "Orders" : "Absent") { Server = "DL_ORDER" });
        Assert.Equal(ambiguous ? ReferenceResolutionKind.Ambiguous : ReferenceResolutionKind.NotFound, resolution.Kind);
        Assert.Equal(Link.Id, resolution.ViaLink?.NodeId);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void A_declared_remote_database_never_falls_through_to_another_database()
    {
        CatalogNode link = Link with { Link = Link.Link! with { Database = "Orders" } };
        CatalogNode[] nodes = [Caller, link, Node("SQL", "Orders", "sales", "Other"), Node("SQL", "Other", "sales", "Orders")];
        ReferenceResolution resolution = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes)).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" });
        Assert.Null(resolution.NodeId);
        Assert.NotNull(resolution.ViaLink);
    }

    [Fact]
    public void A_login_name_alone_cannot_identify_a_remote_server()
    {
        CatalogNode link = Link with { Link = Link.Link! with { DataSource = null } };
        CatalogNode[] nodes = [Caller, link, Node("SQL", "_ServerLevel", null, "entitlements", "Logins")];
        ReferenceResolution resolution = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes)).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" });
        Assert.Equal(ReferenceResolutionKind.External, resolution.Kind);
        Assert.Null(resolution.ViaLink?.TargetServer);
        Assert.Equal("GATEWAY_DEVELOPMENT", resolution.ViaLink?.Metadata?.ConnectIdentifier);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_explicit_database_uses_its_own_mapped_user_schema(bool hasDefaultDatabase)
    {
        CatalogNode link = Link with { Link = Link.Link! with { Database = hasDefaultDatabase ? "Orders" : null, DefaultSchema = hasDefaultDatabase ? "sales" : null } };
        CatalogNode user = Node("SQL", "Other", null, "reader", "Users") with { Principal = new() { Login = "entitlements", DefaultSchema = "archive" } };
        CatalogNode expected = Node("SQL", "Other", "archive", "Orders");
        CatalogNode[] nodes = [Caller, link, user, expected, Node("SQL", "Other", "sales", "Orders")];
        var index = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes));
        Assert.Equal(expected.Id, index.Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER", Database = "Other" }).NodeId);
    }

    [Fact]
    public void Sql_server_links_can_use_an_independent_Oracle_export_and_its_remote_user()
    {
        CatalogNode caller = Node("SQL", "Orders", "dbo", "Report", "Views");
        CatalogNode target = Node("ORA", "PDB", "REMOTE_USER", "Orders", engine: DatabaseEngine.Oracle);
        CatalogNode link = Node("SQL", "_ServerLevel", null, "ORACLE_LINK", "LinkedServers") with
        {
            Link = new() { TargetEngine = DatabaseEngine.Oracle, DataSource = "ORA.example.com:1521/PDB", Database = "PDB", Logins = [new("REMOTE_USER")] },
        };
        CatalogNode[] nodes = [caller, link, target, Node("ORA", "PDB", "OTHER_USER", "Orders", engine: DatabaseEngine.Oracle)];
        var index = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes));
        Assert.Equal(target.Id, index.Resolve(caller, new(null, "Orders") { Server = "ORACLE_LINK" }).NodeId);
    }

    [Fact]
    public void An_explicit_target_engine_prevents_matching_another_engine_with_the_same_address()
    {
        CatalogNode wrong = Node("OTHER", "PDB", "APP", "Orders", engine: DatabaseEngine.Oracle) with
        {
            ServerIdentity = ServerIdentity.FromConfig(new() { Name = "OTHER", Host = "other", ServiceName = "PDB", Type = DatabaseEngine.Oracle, Aliases = ["SQL.example.com,1433"] }),
        };
        var map = LinkedServerMap.FromNodes([Caller, Link, wrong]);
        Assert.Null(map.MetadataFor(Link.Id)?.TargetServer);
    }
}
