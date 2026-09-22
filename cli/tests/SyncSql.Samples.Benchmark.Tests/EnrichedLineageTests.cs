using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Samples.Benchmark.Tests;

public sealed class EnrichedLineageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-enriched-").FullName;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Separate_engine_exports_form_one_path_or_retain_an_unresolved_pointer(bool includeTarget)
    {
        Write("ORACLE", Object("ORA", "PDB", "APP", "REPORT", "Views", DatabaseEngine.Oracle,
            "CREATE VIEW APP.REPORT AS SELECT ID FROM \"RemoteOrders\"@DL_ORDER;"));
        Write("ORACLE", Object("ORA", "PDB", "APP", "DL_ORDER", "DatabaseLinks", DatabaseEngine.Oracle,
            "CREATE DATABASE LINK DL_ORDER CONNECT TO \"entitlements\" USING 'GATEWAY_DEVELOPMENT';") with
        {
            Link = new()
            {
                ConnectIdentifier = "GATEWAY_DEVELOPMENT",
                DataSource = "SQL.example.com,1433",
                TargetEngine = DatabaseEngine.MsSql,
                GatewayHost = "gateway",
                GatewaySid = "orders",
                Logins = [new("entitlements")],
                Evidence = [new("dataSource", "SQL.example.com,1433", "gateway:initorders.ora")]
            },
        });
        if (includeTarget)
        {
            Write("MSSQL", Object("SQL", "_ServerLevel", null, "entitlements", "Logins", DatabaseEngine.MsSql, "-- observed login") with
            { Principal = new() { Login = "entitlements", DefaultDatabase = "Orders" } });
            Write("MSSQL", Object("SQL", "Orders", null, "reader", "Users", DatabaseEngine.MsSql, "-- observed user") with
            { Principal = new() { Login = "entitlements", DefaultSchema = "sales" } });
            Write("MSSQL", Object("SQL", "Orders", "sales", "RemoteOrders", "Views", DatabaseEngine.MsSql,
                "CREATE VIEW sales.RemoteOrders AS SELECT ID FROM sales.Orders;"));
            Write("MSSQL", Object("SQL", "Orders", "sales", "Orders", "Tables", DatabaseEngine.MsSql, "CREATE TABLE sales.Orders (ID int);"));
        }
        Core.Domain.Catalog catalog = await HeterogeneousLineageTests.BuildCatalogAsync(_root);
        var link = Assert.Single(catalog.Nodes, n => n.Type == "DatabaseLinks");
        var caller = Assert.Single(catalog.Nodes, n => n.Name == "REPORT");
        Assert.Contains(catalog.Edges, e => e.From == caller.Id && e.To == link.Id);
        var reference = Assert.Single(catalog.LinkedServerReferences);
        Assert.Equal("SQL.example.com,1433", reference.DataSource);
        Assert.Empty(catalog.OrphanedReferences);
        if (includeTarget)
        {
            var view = Assert.Single(catalog.Nodes, n => n.Name == "RemoteOrders");
            var table = Assert.Single(catalog.Nodes, n => n.Type == "Tables");
            Assert.Contains(catalog.Edges, e => e.From == link.Id && e.To == view.Id);
            Assert.Contains(catalog.Edges, e => e.From == view.Id && e.To == table.Id);
            Assert.Equal(view.Id, reference.To);
            Assert.Equal("resolved", reference.Status);
            Assert.Equal(2, link.Link!.LoginNodeIds.Count);
            Assert.Contains(link.Link.Evidence, evidence => evidence.Source.StartsWith("catalog:", StringComparison.Ordinal));
        }
        else
        {
            Assert.Null(reference.To);
            Assert.Equal("external", reference.Status);
            Assert.Equal("entitlements", Assert.Single(link.Link!.Logins).RemoteUser);
        }
    }

    private static ExtractedObject Object(string server, string database, string? schema, string name, string type, DatabaseEngine engine, string ddl) => new()
    {
        Server = server,
        Database = database,
        Schema = schema,
        Name = name,
        Type = type,
        Engine = engine,
        Ddl = ddl,
        ServerIdentity = ServerIdentity.FromConfig(new() { Name = server, Host = server + ".example.com", Type = engine, ServiceName = engine == DatabaseEngine.Oracle ? database : null }),
    };

    private void Write(string engineFolder, ExtractedObject obj)
    {
        string path = Path.Combine(_root, engineFolder, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ExtractedObjectFile.Write(obj));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
