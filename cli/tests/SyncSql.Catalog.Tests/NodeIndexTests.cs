using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public class NodeIndexTests
{
    private static CatalogNode Node(string server, string database, string? schema, string name, string type = "Tables", DatabaseEngine? engine = null) => new()
    {
        Id = $"{server}/{database}/{type}/{(schema is null ? "" : schema + "/")}{name}",
        Server = server,
        Database = database,
        Schema = schema,
        Type = type,
        Name = name,
        QualifiedName = schema is null ? name : $"{schema}.{name}",
        Path = "irrelevant",
        Ddl = "irrelevant",
        SizeBytes = 0,
        Engine = engine,
    };

    private static CatalogNode LinkNode(string server, string name, string dataSource, string? catalog = null) => new()
    {
        Id = $"{server}/_ServerLevel/LinkedServers/{name}",
        Server = server,
        Database = "_ServerLevel",
        Type = "LinkedServers",
        Name = name,
        QualifiedName = name,
        Path = "irrelevant",
        Ddl = $"EXEC sp_addlinkedserver\n    @server = N'{name}',\n    @datasrc = N'{dataSource}',\n    @catalog = N'{catalog}';",
        SizeBytes = 0,
    };

    [Fact]
    public void Resolve_SchemaQualified_ResolvesWithinServerAndDatabase()
    {
        CatalogNode orders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([orders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(orders.Id, resolution.NodeId);
    }

    [Fact]
    public void Resolve_SchemaQualified_UnknownSchema_IsNotFoundRatherThanGuessing()
    {
        CatalogNode orders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([orders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("sales", "Orders"));

        Assert.Equal(ReferenceResolutionKind.NotFound, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_BareName_UniqueInDatabase_Resolves()
    {
        CatalogNode orders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([orders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(orders.Id, resolution.NodeId);
    }

    [Fact]
    public void Resolve_BareName_AmbiguousAcrossSchemasInDatabase_IsAmbiguousNotNotFound()
    {
        CatalogNode dboOrders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode salesOrders = Node("SQLPROD01", "AppDb", "sales", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([dboOrders, salesOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "Orders"));

        Assert.Equal(ReferenceResolutionKind.Ambiguous, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_BareName_FallsBackToServerScope_WhenUniqueAcrossDatabases()
    {
        CatalogNode remoteOrders = Node("SQLPROD01", "ReportingDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "SyncOrders", type: "StoredProcedures");
        NodeIndex index = new([remoteOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(remoteOrders.Id, resolution.NodeId);
    }

    [Fact]
    public void Resolve_BareName_AmbiguousOnServer_IsAmbiguousNotNotFound()
    {
        CatalogNode appOrders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode reportingOrders = Node("SQLPROD01", "ReportingDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "OtherDb", "dbo", "SyncOrders", type: "StoredProcedures");
        NodeIndex index = new([appOrders, reportingOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "Orders"));

        Assert.Equal(ReferenceResolutionKind.Ambiguous, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_UnknownReference_IsNotFound()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "NoSuchTable"));

        Assert.Equal(ReferenceResolutionKind.NotFound, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_SchemaQualified_MissingLocally_WidensToOtherDatabasesOnSameServer()
    {
        CatalogNode remoteOrders = Node("SQLPROD01", "SalesDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([remoteOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(remoteOrders.Id, resolution.NodeId);
        Assert.Null(resolution.ViaLink);
    }

    [Fact]
    public void Resolve_SchemaQualified_SameNameInTwoOtherDatabases_IsAmbiguous()
    {
        CatalogNode salesOrders = Node("SQLPROD01", "SalesDb", "dbo", "Orders");
        CatalogNode archiveOrders = Node("SQLPROD01", "ArchiveDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([salesOrders, archiveOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders"));

        Assert.Equal(ReferenceResolutionKind.Ambiguous, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_DatabaseQualified_ResolvesInThatDatabase()
    {
        CatalogNode salesOrders = Node("SQLPROD01", "SalesDb", "dbo", "Orders");
        CatalogNode appOrders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([salesOrders, appOrders, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Database = "SalesDb" });

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(salesOrders.Id, resolution.NodeId);
    }

    [Fact]
    public void Resolve_DatabaseQualified_DatabaseInCatalogButObjectMissing_IsNotFound()
    {
        CatalogNode salesCustomers = Node("SQLPROD01", "SalesDb", "dbo", "Customers");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([salesCustomers, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Database = "SalesDb" });

        Assert.Equal(ReferenceResolutionKind.NotFound, resolution.Kind);
    }

    [Fact]
    public void Resolve_DatabaseQualified_DatabaseNotExtracted_IsExternalNotOrphaned()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Database = "NobodyExtractsThis" });

        Assert.Equal(ReferenceResolutionKind.External, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_AcrossLinkedServer_ResolvesOnTheServerTheLinkPointsAt()
    {
        CatalogNode remoteOrders = Node("SQLPROD02", "SalesDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SQLPROD02", "sqlprod02.example.com,1433");
        NodeIndex index = new([remoteOrders, caller, link], LinkedServerMap.FromNodes([remoteOrders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Database = "SalesDb", Server = "SQLPROD02" });

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(remoteOrders.Id, resolution.NodeId);
        Assert.Equal(link.Id, resolution.ViaLink?.NodeId);
    }

    [Fact]
    public void Resolve_AcrossLinkedServer_UsesTheDatabaseTheLinkPinsWhenTheReferenceOmitsOne()
    {
        CatalogNode remoteOrders = Node("SQLPROD02", "SalesDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SALES_LINK", "SQLPROD02", catalog: "SalesDb");
        NodeIndex index = new([remoteOrders, caller, link], LinkedServerMap.FromNodes([remoteOrders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Server = "SALES_LINK" });

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(remoteOrders.Id, resolution.NodeId);
    }

    [Fact]
    public void Resolve_AcrossUnknownLinkedServer_IsExternalNotOrphaned()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([caller], LinkedServerMap.FromNodes([caller]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Server = "SOME_OTHER_FLEET" });

        Assert.Equal(ReferenceResolutionKind.External, resolution.Kind);
        Assert.Null(resolution.ViaLink);
    }

    [Fact]
    public void Resolve_UnqualifiedByServer_FallsBackToALinkedServerWhenNothingLocalMatches()
    {
        CatalogNode remoteOrders = Node("SQLPROD02", "SalesDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SQLPROD02", "sqlprod02");
        NodeIndex index = new([remoteOrders, caller, link], LinkedServerMap.FromNodes([remoteOrders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(remoteOrders.Id, resolution.NodeId);
        Assert.Equal(link.Id, resolution.ViaLink?.NodeId);
    }

    [Fact]
    public void Resolve_LocalMatchWins_OverALinkedServerCandidate()
    {
        CatalogNode localOrders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode remoteOrders = Node("SQLPROD02", "SalesDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SQLPROD02", "sqlprod02");
        NodeIndex index = new([localOrders, remoteOrders, caller, link], LinkedServerMap.FromNodes([localOrders, remoteOrders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(localOrders.Id, resolution.NodeId);
        Assert.Null(resolution.ViaLink);
    }

    [Fact]
    public void Resolve_BareName_DoesNotCrossALinkedServer()
    {
        CatalogNode remoteOrders = Node("SQLPROD02", "SalesDb", null, "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SQLPROD02", "sqlprod02");
        NodeIndex index = new([remoteOrders, caller, link], LinkedServerMap.FromNodes([remoteOrders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(null, "Orders"));

        Assert.Equal(ReferenceResolutionKind.NotFound, resolution.Kind);
    }

    [Fact]
    public void Resolve_FourPartNameSpellingOutItsOwnServer_StaysLocal()
    {
        CatalogNode orders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([orders, caller]);

        ReferenceResolution resolution = index.Resolve(
            caller, new ObjectRef("dbo", "Orders") { Database = "AppDb", Server = "SQLPROD01" });

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(orders.Id, resolution.NodeId);
        Assert.Null(resolution.ViaLink);
    }

    [Fact]
    public void Resolve_AcrossALoopbackLink_StaysLocalWithoutALinkHop()
    {
        CatalogNode orders = Node("SQLPROD01", "AppDb", "dbo", "Orders");
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        CatalogNode link = LinkNode("SQLPROD01", "SELFLINK", "SQLPROD01", catalog: "AppDb");
        NodeIndex index = new([orders, caller, link], LinkedServerMap.FromNodes([orders, caller, link]));

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "Orders") { Server = "SELFLINK" });

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(orders.Id, resolution.NodeId);
        Assert.Null(resolution.ViaLink);
    }

    [Theory]
    [InlineData(null, "sp_executesql")]
    [InlineData(null, "xp_cmdshell")]
    [InlineData("sys", "objects")]
    [InlineData("INFORMATION_SCHEMA", "COLUMNS")]
    [InlineData("dbo", "sp_who2")]
    public void Resolve_EngineProvidedObject_IsSystemRatherThanAnOrphan(string? schema, string name)
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures", engine: DatabaseEngine.MsSql);
        NodeIndex index = new([caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef(schema, name));

        Assert.Equal(ReferenceResolutionKind.System, resolution.Kind);
        Assert.Null(resolution.NodeId);
    }

    [Fact]
    public void Resolve_SystemDatabaseQualifiedBuiltin_IsSystemEvenWhenMasterIsNotExtracted()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures", engine: DatabaseEngine.MsSql);
        NodeIndex index = new([caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "xp_cmdshell") { Database = "master" });

        Assert.Equal(ReferenceResolutionKind.System, resolution.Kind);
    }

    /// <summary>
    /// The built-in rules are only ever consulted after the real lookup fails, so somebody's own
    /// procedure that borrows a reserved-looking prefix still resolves to itself.
    /// </summary>
    [Fact]
    public void Resolve_UserObjectWithASystemLookingName_StillResolvesToItself()
    {
        CatalogNode helper = Node("SQLPROD01", "AppDb", "dbo", "sp_MyHelper", type: "StoredProcedures", engine: DatabaseEngine.MsSql);
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures", engine: DatabaseEngine.MsSql);
        NodeIndex index = new([helper, caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("dbo", "sp_MyHelper"));

        Assert.Equal(ReferenceResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(helper.Id, resolution.NodeId);
    }

    /// <summary>A prefixed name under somebody's own schema is a user object, and a missing one is worth reporting.</summary>
    [Fact]
    public void Resolve_SystemPrefixUnderAUserSchema_IsStillAnOrphan()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures", engine: DatabaseEngine.MsSql);
        NodeIndex index = new([caller]);

        ReferenceResolution resolution = index.Resolve(caller, new ObjectRef("app", "sp_Nightly"));

        Assert.Equal(ReferenceResolutionKind.NotFound, resolution.Kind);
    }

    [Fact]
    public void Resolve_OracleBuiltinPackage_IsSystem()
    {
        CatalogNode caller = Node("ORCL01", "APPDB", "APP", "LOAD_ORDERS", type: "Procedures", engine: DatabaseEngine.Oracle);
        NodeIndex index = new([caller]);

        Assert.Equal(ReferenceResolutionKind.System, index.Resolve(caller, new ObjectRef(null, "DBMS_OUTPUT")).Kind);
        Assert.Equal(ReferenceResolutionKind.System, index.Resolve(caller, new ObjectRef("SYS", "DUAL")).Kind);
    }

    /// <summary>A node with no engine tag gets no built-in treatment - the same "don't guess" posture lineage inference already takes for it.</summary>
    [Fact]
    public void Resolve_UntaggedEngine_DoesNotApplyBuiltinRules()
    {
        CatalogNode caller = Node("SQLPROD01", "AppDb", "dbo", "GetOrder", type: "StoredProcedures");
        NodeIndex index = new([caller]);

        Assert.Equal(ReferenceResolutionKind.NotFound, index.Resolve(caller, new ObjectRef(null, "sp_executesql")).Kind);
    }
}
