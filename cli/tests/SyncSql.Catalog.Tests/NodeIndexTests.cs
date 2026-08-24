using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public class NodeIndexTests
{
    private static CatalogNode Node(string server, string database, string? schema, string name, string type = "Tables") => new()
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
}
