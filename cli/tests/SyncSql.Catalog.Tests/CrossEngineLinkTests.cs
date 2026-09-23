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

    [Theory]
    [InlineData(DatabaseEngine.MsSql, "sales", true)]
    [InlineData(DatabaseEngine.MsSql, "sales", false)]
    [InlineData(DatabaseEngine.MsSql, "dbo", false)]
    [InlineData(DatabaseEngine.Oracle, "APP", false)]
    public void A_known_remote_schema_only_falls_back_to_dbo_on_sql_server(DatabaseEngine engine, string schema, bool hasDbo)
    {
        CatalogNode link = Link with { Link = Link.Link! with { TargetEngine = engine, DataSource = "SQL", Database = "Orders", DefaultSchema = schema } };
        CatalogNode marker = Node("SQL", "Orders", "other", "Marker", engine: engine);
        CatalogNode table = Node("SQL", "Orders", "dbo", hasDbo ? "Wanted" : "Other", engine: engine);
        CatalogNode[] nodes = [Caller, link, marker, table];
        ReferenceResolution result = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes)).Resolve(Caller, new(null, "Wanted") { Server = "DL_ORDER" });
        Assert.Equal(hasDbo ? table.Id : null, result.NodeId);
        Assert.Equal(hasDbo ? ReferenceResolutionKind.Resolved : ReferenceResolutionKind.NotFound, result.Kind);
        Assert.Equal(link.Id, result.ViaLink?.NodeId);
    }

    [Fact]
    public void Unextracted_remote_database_uses_the_destination_engine_for_system_objects()
    {
        CatalogNode[] nodes = [Caller, Link, Node("SQL", "Orders", "dbo", "Marker")];
        var index = new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes));
        Assert.Equal(ReferenceResolutionKind.System, index.Resolve(Caller, new("dbo", "xp_cmdshell") { Server = "DL_ORDER", Database = "master", IsRoutine = true }).Kind);
        ReferenceResolution unknown = index.Resolve(Caller, new("dbo", "Absent") { Server = "DL_ORDER", Database = "Missing" });
        Assert.Equal(ReferenceResolutionKind.External, unknown.Kind);
        Assert.NotNull(unknown.ViaLink);
    }

    [Fact]
    public void Legacy_Oracle_ddl_preserves_escaped_remote_login_and_infers_engine_from_identity()
    {
        CatalogNode legacy = Link with { Link = null, Ddl = "CREATE DATABASE LINK DL_ORDER CONNECT TO \"read\"\"er\" USING 'SQL';" };
        CatalogNode target = Node("SQL", "Orders", "dbo", "Orders") with { Engine = null };
        LinkMetadata metadata = LinkedServerMap.FromNodes([Caller, legacy, target]).MetadataFor(legacy.Id)!;
        Assert.Equal("read\"er", Assert.Single(metadata.Logins).RemoteUser);
        Assert.Equal(DatabaseEngine.MsSql, metadata.TargetEngine);
        Assert.Equal("SQL", metadata.TargetServer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_login_defaults_do_not_invent_a_database(string? database)
    {
        CatalogNode login = Node("SQL", "_ServerLevel", null, "entitlements", "Logins") with
        { Principal = database is null ? null : new() { DefaultDatabase = database } };
        LinkMetadata metadata = LinkedServerMap.FromNodes([Caller, Link, login]).MetadataFor(Link.Id)!;
        Assert.Null(metadata.Database);
        Assert.Null(metadata.DefaultSchema);
        Assert.Contains(login.Id, metadata.LoginNodeIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_mapped_user_defaults_do_not_invent_a_schema(string? schema)
    {
        CatalogNode link = Link with { Link = Link.Link! with { Database = "Orders" } };
        CatalogNode user = Node("SQL", "Orders", null, "reader", "Users") with { Principal = new() { Login = "entitlements", DefaultSchema = schema } };
        CatalogNode table = Node("SQL", "Orders", "dbo", "Orders");
        CatalogNode[] nodes = [Caller, link, user, table];
        var map = LinkedServerMap.FromNodes(nodes);
        Assert.Null(map.MetadataFor(link.Id)!.DefaultSchema);
        Assert.Equal(table.Id, new NodeIndex(nodes, map).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" }).NodeId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Incomplete_or_context_dependent_login_mappings_cannot_choose_a_default_schema(int kind)
    {
        LinkLogin[] logins = kind switch { 0 => [new(null), new("")], 1 => [new("entitlements"), new("", UsesSelf: true)], _ => [new("entitlements"), new("other")] };
        CatalogNode link = Link with { Link = Link.Link! with { Database = "Orders", Logins = logins } };
        CatalogNode[] nodes = [Caller, link, Node("SQL", "Orders", "dbo", "Orders"), Node("SQL", "Orders", "sales", "Orders")];
        var map = LinkedServerMap.FromNodes(nodes);
        Assert.Null(map.MetadataFor(link.Id)!.DefaultSchema);
        Assert.Equal(ReferenceResolutionKind.Ambiguous, new NodeIndex(nodes, map).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" }).Kind);
    }

    [Fact]
    public void Schema_lookup_ignores_users_in_other_servers_databases_or_logins()
    {
        CatalogNode link = Link with { Link = Link.Link! with { Database = "Orders" } };
        CatalogNode table = Node("SQL", "Orders", "dbo", "Orders");
        CatalogNode[] nodes = [Caller, link, table,
            Node("SQL", "Orders", null, "unmapped", "Users"),
            Node("SQL", "Orders", null, "different", "Users") with { Principal = new() { Login = "other", DefaultSchema = "wrong" } },
            Node("SQL", "Other", null, "reader", "Users") with { Principal = new() { Login = "entitlements", DefaultSchema = "wrong" } },
            Node("OtherServer", "Orders", null, "reader", "Users") with { Principal = new() { Login = "entitlements", DefaultSchema = "wrong" } }];
        var map = LinkedServerMap.FromNodes(nodes);
        Assert.Equal(table.Id, new NodeIndex(nodes, map).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER" }).NodeId);
    }

    [Fact]
    public void Unknown_engine_does_not_infer_Oracle_or_sql_default_schema()
    {
        CatalogNode link = Link with { Link = Link.Link! with { TargetEngine = null, DataSource = "SQL" } };
        CatalogNode target = Node("SQL", "Orders", "dbo", "Orders") with { Engine = null, ServerIdentity = null };
        LinkMetadata metadata = LinkedServerMap.FromNodes([Caller, link, target]).MetadataFor(link.Id)!;
        Assert.Null(metadata.TargetEngine);
        Assert.Null(metadata.DefaultSchema);
        // An explicitly stated engine cannot match a target with no engine evidence.
        Assert.Null(LinkedServerMap.FromNodes([Caller, Link, target]).MetadataFor(link.Id)!.TargetServer);
    }

    [Fact]
    public void A_schema_mapping_without_a_default_database_applies_to_an_explicit_database()
    {
        CatalogNode link = Link with { Link = Link.Link! with { DefaultSchema = "sales" } };
        CatalogNode table = Node("SQL", "Orders", "sales", "Orders");
        CatalogNode[] nodes = [Caller, link, table, Node("SQL", "Orders", "dbo", "Orders")];
        Assert.Equal(table.Id, new NodeIndex(nodes, LinkedServerMap.FromNodes(nodes)).Resolve(Caller, new(null, "Orders") { Server = "DL_ORDER", Database = "Orders" }).NodeId);
    }

    [Fact]
    public void Explicit_engine_matching_can_use_server_identity_when_the_node_engine_is_missing()
    {
        CatalogNode target = Node("SQL", "Orders", "dbo", "Orders") with { Engine = null };
        Assert.Equal("SQL", LinkedServerMap.FromNodes([Caller, Link, target]).MetadataFor(Link.Id)!.TargetServer);
    }
}
