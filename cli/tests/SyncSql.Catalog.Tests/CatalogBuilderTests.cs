using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Catalog.Tests;

public sealed class CatalogBuilderTests : IDisposable
{
    [Fact]
    public async Task BuildAsync_Passes_each_objects_Broker_context_to_its_analyzer()
    {
        Guid brokerGuid = Guid.Parse("aabbccdd-1111-2222-3333-444444444444");
        foreach (var context in new[] { (Database: "App", Guid: (Guid?)brokerGuid), (Database: "Legacy", Guid: (Guid?)null) })
        {
            ExtractedObject obj = new()
            {
                Server = "SQL",
                Database = context.Database,
                Schema = "dbo",
                Type = "StoredProcedures",
                Name = "Send",
                Ddl = $"CREATE PROCEDURE dbo.Send AS PRINT '{context.Database}';",
                Engine = DatabaseEngine.MsSql,
                ServiceBrokerGuid = context.Guid,
            };
            string path = Path.Combine(_objectsRoot, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ExtractedObjectFile.Write(obj));
        }
        Core.Domain.Catalog catalog = await CreateBuilder().BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot, DynamicSql = false }, CancellationToken.None);
        foreach (CatalogNode node in catalog.Nodes)
        {
            Assert.Equal(node.Database == "App" ? brokerGuid : (Guid?)null, node.ServiceBrokerGuid);
            _mssqlAnalyzer.Received(1).Analyze(node.Ddl, Arg.Is<LineageAnalysisOptions>(o => o.ServiceBrokerGuid == node.ServiceBrokerGuid && !o.DynamicSql));
        }
    }

    [Fact]
    public async Task BuildAsync_NestedLinkedServer_PreservesRemoteIdentityAndResolvesReference()
    {
        WriteObjectFile("ROOT", "_ServerLevel", "LinkedServers", null, "REMOTE", LinkedServerDdl("REMOTE", "host.example.com", "SalesDb"));
        WriteObjectFile("ROOT", "AppDb", "Views", "dbo", "Orders", "SELECT * FROM REMOTE.SalesDb.sales.Orders;");
        ExtractedObject remote = new()
        {
            Server = "REMOTE_2",
            Database = "SalesDb",
            Schema = "sales",
            Type = "Tables",
            Name = "Orders",
            Ddl = "CREATE TABLE sales.Orders (Id int);",
            Engine = DatabaseEngine.MsSql,
        };
        string relative = ExtractedObjectFile.RelativePath(remote.Server, remote.Database, remote.Schema, remote.Type, remote.Name,
            serverPath: ["ROOT", "LinkedServers", "REMOTE"]);
        string path = Path.Combine(_objectsRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ExtractedObjectFile.Write(remote));
        _mssqlAnalyzer.Analyze(Arg.Is<string>(sql => sql.Contains("SELECT", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("sales", "Orders") { Database = "SalesDb", Server = "REMOTE" }],
            Aliases = new Dictionary<string, ObjectRef>(),
            ColumnRefs = [],
        });
        Core.Domain.Catalog catalog = await CreateBuilder().BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);
        CatalogNode target = Assert.Single(catalog.Nodes, node => node.Server == "REMOTE_2");
        Assert.Equal(relative, target.Path);
        Assert.Equal("REMOTE_2/SalesDb/Tables/sales/Orders", target.Id);
        Assert.Contains(catalog.Edges, edge => edge.To == target.Id);
        Assert.Equal(target.Id, Assert.Single(catalog.LinkedServerReferences).To);
    }

    [Fact]
    public async Task BuildAsync_SchemaDefinitionInsideSchemaFolder_RetainsLegacyIdentity()
    {
        WriteObjectFile("ROOT", "AppDb", "Schemas", null, "sales", "CREATE SCHEMA sales AUTHORIZATION SalesOwner;");
        Core.Domain.Catalog catalog = await CreateBuilder().BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);
        CatalogNode schema = Assert.Single(catalog.Nodes);
        Assert.Equal("ROOT/AppDb/sales/sales.sql", schema.Path);
        Assert.Equal("ROOT/AppDb/Schemas/sales", schema.Id);
        Assert.Equal("Schemas", schema.Type);
        Assert.Null(schema.Schema);
    }
    private readonly string _objectsRoot = Directory.CreateTempSubdirectory("syncsql-objects-").FullName;
    private readonly ILineageAnalyzerResolver _lineageAnalyzerResolver = Substitute.For<ILineageAnalyzerResolver>();
    private readonly ILineageAnalyzer _mssqlAnalyzer = Substitute.For<ILineageAnalyzer>();
    private readonly IGitHistoryMiner _gitHistoryMiner = Substitute.For<IGitHistoryMiner>();
    private readonly IMetricsHistoryStore _metricsHistoryStore = Substitute.For<IMetricsHistoryStore>();
    private readonly TestTimeProvider _timeProvider = new(FixedNow);
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-01T12:00:00Z");

    public CatalogBuilderTests()
    {
        _mssqlAnalyzer.Engine.Returns(DatabaseEngine.MsSql);
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(LineageAnalysisResult.Empty);
        _lineageAnalyzerResolver.Resolve(DatabaseEngine.MsSql).Returns(_mssqlAnalyzer);
    }

    private CatalogBuilder CreateBuilder() => new(
        _lineageAnalyzerResolver,
        _gitHistoryMiner,
        _metricsHistoryStore,
        _timeProvider,
        NullLogger<CatalogBuilder>.Instance);

    private void WriteObjectFile(
        string server,
        string database,
        string type,
        string? schema,
        string name,
        string ddl,
        DatabaseEngine? engine = DatabaseEngine.MsSql,
        IReadOnlyList<ExtractedColumn>? columns = null)
    {
        string relative = ExtractedObjectFile.RelativePath(server, database, schema, type, name);
        string path = Path.Combine(_objectsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (engine is { } definiteEngine)
        {
            ExtractedObject obj = new()
            {
                Server = server,
                Database = database,
                Schema = schema,
                Type = type,
                Name = name,
                Ddl = ddl,
                Engine = definiteEngine,
                Columns = columns ?? [],
            };
            File.WriteAllText(path, ExtractedObjectFile.Write(obj));
        }
        else
        {
            // No "-- Engine:" header at all - simulates a file predating that field.
            File.WriteAllText(path, $"-- Server:   {server}\n-- Database: {database}\n\n{ddl}\n");
        }
    }

    private static string LinkedServerDdl(string name, string dataSource, string catalog) => $"""
        EXEC sp_addlinkedserver
            @server = N'{name}',
            @srvproduct = N'SQL Server',
            @provider = N'SQLNCLI',
            @datasrc = N'{dataSource}',
            @provstr = N'',
            @catalog = N'{catalog}';
        """;

    private static string NodeId(string server, string database, string type, string? schema, string name) =>
        ExtractedObjectFile.ObjectId(server, database, schema, type, name);

    [Fact]
    public async Task BuildAsync_MissingObjectsRoot_ThrowsDirectoryNotFoundException()
    {
        CatalogBuilder builder = CreateBuilder();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => builder.BuildAsync(
            new CatalogBuildRequest { ObjectsRoot = Path.Combine(_objectsRoot, "does-not-exist") },
            CancellationToken.None));
    }

    [Theory]
    [InlineData("SQLPROD01/LinkedServers/REMOTE.sql")]
    [InlineData("SQLPROD01/_ServerLevel/LinkedServers/REMOTE.sql")]
    public async Task BuildAsync_ServerLevelLayouts_PreserveIdentityAndHistoryMapping(string relative)
    {
        string path = Path.Combine(_objectsRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ExtractedObjectFile.Write(new ExtractedObject
        {
            Server = "SQLPROD01",
            Database = "_ServerLevel",
            Type = "LinkedServers",
            Name = "REMOTE",
            Ddl = LinkedServerDdl("REMOTE", "remote.example.com", "AppDb"),
            Engine = DatabaseEngine.MsSql,
        }));
        _gitHistoryMiner.MineAsync(Arg.Any<GitHistoryMiningRequest>(), Arg.Any<CancellationToken>())
            .Returns(GitHistoryMiningResult.Empty);

        Core.Domain.Catalog catalog = await CreateBuilder().BuildAsync(
            new CatalogBuildRequest { ObjectsRoot = _objectsRoot, RepoRoot = _objectsRoot }, CancellationToken.None);

        CatalogNode node = Assert.Single(catalog.Nodes);
        Assert.Equal("SQLPROD01/_ServerLevel/LinkedServers/REMOTE", node.Id);
        Assert.Equal("_ServerLevel", node.Database);
        Assert.Equal("LinkedServers", node.Type);
        Assert.Null(node.Schema);
        Assert.Equal(relative, node.Path);
        await _gitHistoryMiner.Received(1).MineAsync(
            Arg.Is<GitHistoryMiningRequest>(request => request.ObjectPaths[relative] == node.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_EmptyTree_ProducesEmptyCatalogAtGeneratedAtFromTimeProvider()
    {
        CatalogBuilder builder = CreateBuilder();

        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Nodes);
        Assert.Empty(catalog.Edges);
        Assert.Equal(FixedNow, catalog.GeneratedAt);
    }

    [Fact]
    public async Task BuildAsync_ScansObjectTree_CreatesOneNodePerFileWithTypeCounts()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Customers", "CREATE TABLE dbo.Customers (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder", "CREATE PROCEDURE dbo.GetOrder AS SELECT 1;");
        CatalogBuilder builder = CreateBuilder();

        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Equal(3, catalog.Nodes.Count);
        Assert.Equal(2, catalog.TypeCounts["Tables"]);
        Assert.Equal(1, catalog.TypeCounts["StoredProcedures"]);
        Assert.Equal(["SQLPROD01"], catalog.Servers);
    }

    [Fact]
    public async Task BuildAsync_ObjectReferencingAnother_InfersEdgeWithColumnTags()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders",
            "CREATE TABLE dbo.Orders (Id INT, CustomerId INT);",
            columns: [new ExtractedColumn("Id", "int", null), new ExtractedColumn("CustomerId", "int", null)]);
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT o.Id, o.CustomerId FROM dbo.Orders o;");

        LineageAnalysisResult referencesOrders = new()
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase) { ["o"] = new ObjectRef("dbo", "Orders") },
            ColumnRefs = [new ColumnRef("o", "Id"), new ColumnRef("o", "CustomerId")],
        };
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("FROM dbo.Orders", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(referencesOrders);

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        string ordersId = NodeId("SQLPROD01", "AppDb", "Tables", "dbo", "Orders");
        string procId = NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder");
        CatalogEdge edge = Assert.Single(catalog.Edges);
        Assert.Equal(procId, edge.From);
        Assert.Equal(ordersId, edge.To);
        Assert.Equal(["CustomerId", "Id"], edge.Columns);
    }

    [Fact]
    public async Task BuildAsync_UnresolvableReference_ProducesNoEdgeButFlagsOrphanedReference()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.NoSuchTable;");
        string procId = NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "NoSuchTable")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Edges);
        CatalogOrphanedReference orphan = Assert.Single(catalog.OrphanedReferences);
        Assert.Equal(procId, orphan.From);
        Assert.Equal("dbo", orphan.Schema);
        Assert.Equal("NoSuchTable", orphan.Name);
    }

    [Fact]
    public async Task BuildAsync_DuplicateUnresolvableReferences_ProduceOnlyOneOrphanedEntry()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.NoSuchTable; SELECT 2 FROM dbo.NoSuchTable;");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "NoSuchTable"), new ObjectRef("dbo", "NoSuchTable")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Single(catalog.OrphanedReferences);
    }

    [Fact]
    public async Task BuildAsync_AmbiguousBareReference_IsNotFlaggedAsOrphaned()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "sales", "Orders", "CREATE TABLE sales.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef(null, "Orders")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Edges);
        Assert.Empty(catalog.OrphanedReferences);
    }

    [Fact]
    public async Task BuildAsync_MultipleOrphanedReferences_AreSortedByFromThenName()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "Zeta", "CREATE PROCEDURE dbo.Zeta AS SELECT 1 FROM dbo.Missing;");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "Alpha", "CREATE PROCEDURE dbo.Alpha AS SELECT 1 FROM dbo.Missing;");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Missing")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Equal(2, catalog.OrphanedReferences.Count);
        Assert.Equal(NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "Alpha"), catalog.OrphanedReferences[0].From);
        Assert.Equal(NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "Zeta"), catalog.OrphanedReferences[1].From);
    }

    [Fact]
    public async Task BuildAsync_DuplicateObjectRefs_ProduceOnlyOneEdge()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.Orders; SELECT 2 FROM dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders"), new ObjectRef("dbo", "Orders")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Single(catalog.Edges);
    }

    [Fact]
    public async Task BuildAsync_ObjectFileWithoutEngineHeader_SkipsLineageInferenceWithoutError()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Legacy", "CREATE TABLE dbo.Legacy (Id INT);", engine: null);
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(_ => throw new InvalidOperationException("lineage should not run for a node with no engine"));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Single(catalog.Nodes);
        Assert.Null(catalog.Nodes[0].Engine);
        Assert.Empty(catalog.Edges);
    }

    [Fact]
    public async Task BuildAsync_MetricsRootProvided_AttachesHistoryPerNode()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        string ordersId = NodeId("SQLPROD01", "AppDb", "Tables", "dbo", "Orders");
        MetricsSnapshot snapshot = new() { CapturedAt = FixedNow, RowCount = 42 };
        _metricsHistoryStore.LoadHistoryAsync("metrics-root", ordersId, Arg.Any<CancellationToken>()).Returns([snapshot]);

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(
            new CatalogBuildRequest { ObjectsRoot = _objectsRoot, MetricsRoot = "metrics-root" }, CancellationToken.None);

        Assert.Single(catalog.Nodes[0].Metrics);
        Assert.Equal(42, catalog.Nodes[0].Metrics[0].RowCount);
    }

    [Fact]
    public async Task BuildAsync_NoMetricsRoot_LeavesMetricsHistoryStoreUntouched()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        CatalogBuilder builder = CreateBuilder();

        await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        await _metricsHistoryStore.DidNotReceiveWithAnyArgs().LoadHistoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task BuildAsync_RepoRootProvided_AttachesGitHistoryAndTopLevelFields()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        string ordersId = NodeId("SQLPROD01", "AppDb", "Tables", "dbo", "Orders");
        CatalogCommit commit = new() { Sha = "abc123", Date = FixedNow, Message = "init", ObjectIds = [ordersId] };
        CoChangePair pair = new() { A = ordersId, B = "other", Count = 3 };
        _gitHistoryMiner.MineAsync(Arg.Is<GitHistoryMiningRequest>(r => r.RepoRoot == "repo-root"), Arg.Any<CancellationToken>())
            .Returns(new GitHistoryMiningResult
            {
                RecentChanges = [commit],
                CoChangePairs = [pair],
                ObjectHistory = new Dictionary<string, ObjectHistoryInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    [ordersId] = new ObjectHistoryInfo { ChangeCount = 5, LastChangedAt = FixedNow, Versions = [] },
                },
            });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(
            new CatalogBuildRequest { ObjectsRoot = _objectsRoot, RepoRoot = "repo-root" }, CancellationToken.None);

        Assert.Single(catalog.RecentChanges);
        Assert.Single(catalog.CoChangePairs);
        Assert.Equal(5, catalog.Nodes[0].ChangeCount);
        Assert.Equal(FixedNow, catalog.Nodes[0].LastChangedAt);
    }

    [Fact]
    public async Task BuildAsync_NoRepoRoot_LeavesGitHistoryMinerUntouched()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        CatalogBuilder builder = CreateBuilder();

        await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        await _gitHistoryMiner.DidNotReceiveWithAnyArgs().MineAsync(default!, default);
    }

    [Fact]
    public async Task BuildAsync_SchemalessObjectPath_LoadsWithNullSchema()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "LinkedServers", null, "REMOTESRV", "EXEC sp_addlinkedserver 'REMOTESRV';");
        CatalogBuilder builder = CreateBuilder();

        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogNode node = Assert.Single(catalog.Nodes);
        Assert.Null(node.Schema);
        Assert.Equal("REMOTESRV", node.QualifiedName);
    }

    [Fact]
    public async Task BuildAsync_CrossDatabaseReference_ResolvesToTheOtherDatabaseOnTheSameServer()
    {
        WriteObjectFile("SQLPROD01", "SalesDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM SalesDb.dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders") { Database = "SalesDb" }],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogEdge edge = Assert.Single(catalog.Edges);
        Assert.Equal(NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder"), edge.From);
        Assert.Equal(NodeId("SQLPROD01", "SalesDb", "Tables", "dbo", "Orders"), edge.To);
        Assert.Empty(catalog.OrphanedReferences);
    }

    [Fact]
    public async Task BuildAsync_ReferenceIntoAnUnextractedDatabase_IsNotFlaggedAsOrphaned()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM NobodyExtractsThis.dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders") { Database = "NobodyExtractsThis" }],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Edges);
        Assert.Empty(catalog.OrphanedReferences);
    }

    [Fact]
    public async Task BuildAsync_ReferenceAcrossALinkedServer_RoutesLineageThroughTheLinkAndListsItThere()
    {
        WriteObjectFile("SQLPROD01", "_ServerLevel", "LinkedServers", null, "SALES_LINK", LinkedServerDdl("SALES_LINK", "SQLPROD02", "SalesDb"));
        WriteObjectFile("SQLPROD02", "SalesDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM SALES_LINK.SalesDb.dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders") { Database = "SalesDb", Server = "SALES_LINK" }],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        string procId = NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder");
        string linkId = NodeId("SQLPROD01", "_ServerLevel", "LinkedServers", null, "SALES_LINK");
        string ordersId = NodeId("SQLPROD02", "SalesDb", "Tables", "dbo", "Orders");

        Assert.Contains(catalog.Edges, e => e.From == procId && e.To == linkId);
        Assert.Contains(catalog.Edges, e => e.From == linkId && e.To == ordersId);
        Assert.DoesNotContain(catalog.Edges, e => e.From == procId && e.To == ordersId);
        Assert.Empty(catalog.OrphanedReferences);

        CatalogLinkedServerReference reference = Assert.Single(catalog.LinkedServerReferences);
        Assert.Equal(linkId, reference.LinkedServer);
        Assert.Equal(procId, reference.From);
        Assert.Equal(ordersId, reference.To);
        Assert.Equal("SalesDb", reference.Database);
        Assert.Equal("dbo", reference.Schema);
        Assert.Equal("Orders", reference.Name);
    }

    [Fact]
    public async Task BuildAsync_ReferenceAcrossALinkToAnUnextractedServer_IsListedOnTheLinkWithoutATarget()
    {
        WriteObjectFile("SQLPROD01", "_ServerLevel", "LinkedServers", null, "VENDOR", LinkedServerDdl("VENDOR", "vendor-host.example.net", "VendorDb"));
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM VENDOR.VendorDb.dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "Orders") { Database = "VendorDb", Server = "VENDOR" }],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        string procId = NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder");
        string linkId = NodeId("SQLPROD01", "_ServerLevel", "LinkedServers", null, "VENDOR");

        CatalogEdge edge = Assert.Single(catalog.Edges);
        Assert.Equal(procId, edge.From);
        Assert.Equal(linkId, edge.To);
        Assert.Empty(catalog.OrphanedReferences);

        CatalogLinkedServerReference reference = Assert.Single(catalog.LinkedServerReferences);
        Assert.Equal(linkId, reference.LinkedServer);
        Assert.Null(reference.To);
        Assert.Equal("VendorDb", reference.Database);
    }

    [Fact]
    public async Task BuildAsync_NestedTree_ProducesForwardSlashIdsAndPathsOnEveryPlatform()
    {
        // The tree is walked with the platform's own separator (a backslash on Windows) but its ids
        // are the keys the site, the metrics snapshots and the git history all join on, so they
        // stay '/'-separated no matter which OS built the catalog.
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        CatalogBuilder builder = CreateBuilder();

        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogNode node = Assert.Single(catalog.Nodes);
        Assert.Equal("SQLPROD01/AppDb/Tables/dbo/Orders", node.Id);
        Assert.Equal("SQLPROD01/AppDb/dbo/Tables/Orders.sql", node.Path);
        Assert.DoesNotContain('\\', node.Id);
        Assert.DoesNotContain('\\', node.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAsync_LegacyAndSchemaFirstLayouts_HaveTheSameIdentity(bool schemaFirst)
    {
        // A schema named like an object type must not require guessing from directory names.
        string relative = schemaFirst ? "SRV/DB/Views/Tables/Orders.sql" : "SRV/DB/Tables/Views/Orders.sql";
        string path = Path.Combine(_objectsRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string marker = schemaFirst ? "-- Path layout: schema/type\n" : string.Empty;
        await File.WriteAllTextAsync(path, marker + "-- Engine: mssql\n\nCREATE TABLE Views.Orders (Id INT);");
        var catalog = await CreateBuilder().BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);
        var node = Assert.Single(catalog.Nodes);
        Assert.Equal("SRV/DB/Tables/Views/Orders", node.Id);
        Assert.Equal("Views", node.Schema);
        Assert.Equal("Tables", node.Type);
        Assert.Equal(relative, node.Path);
    }

    [Fact]
    public async Task BuildAsync_ObjectFileWrittenWithAUtf8Bom_StillParsesHeaderAndBody()
    {
        // A tree extracted by the PowerShell version this CLI replaced: same format, BOM in front.
        ExtractedObject obj = new()
        {
            Server = "SQLPROD01",
            Database = "AppDb",
            Schema = "dbo",
            Type = "Tables",
            Name = "Orders",
            Ddl = "CREATE TABLE dbo.Orders (Id INT);",
            Engine = DatabaseEngine.MsSql,
            Description = "Cabeçalho do pedido",
        };
        string path = Path.Combine(_objectsRoot, ExtractedObjectFile.RelativePath("SQLPROD01", "AppDb", "dbo", "Tables", "Orders").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ExtractedObjectFile.Write(obj), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogNode node = Assert.Single(catalog.Nodes);
        Assert.Equal("CREATE TABLE dbo.Orders (Id INT);", node.Ddl);
        Assert.Equal("Cabeçalho do pedido", node.Description);
    }

    private static LineageAnalysisResult Refs(params ObjectRef[] objectRefs) => new()
    {
        ObjectRefs = objectRefs,
        Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
        ColumnRefs = [],
    };

    [Fact]
    public async Task BuildAsync_SystemObjectReference_IsRecordedRatherThanFlaggedAsOrphaned()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS EXEC sp_executesql N'SELECT 1';");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>()).Returns(Refs(new ObjectRef(null, "sp_executesql")));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.OrphanedReferences);
        Assert.Empty(catalog.Edges);
        CatalogSystemReference system = Assert.Single(catalog.SystemReferences);
        Assert.Equal(NodeId("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder"), system.From);
        Assert.Equal("sp_executesql", system.Name);
    }

    [Fact]
    public async Task BuildAsync_DynamicReferenceThatResolvesNowhere_IsNotFlaggedAsOrphaned()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS EXEC ('SELECT 1 FROM dbo.MaybeNotATable');");
        _mssqlAnalyzer.Analyze(Arg.Any<string>(), Arg.Any<LineageAnalysisOptions?>())
            .Returns(Refs(new ObjectRef("dbo", "MaybeNotATable") { Origin = ReferenceOrigin.Dynamic }));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Edges);
        Assert.Empty(catalog.OrphanedReferences);
    }

    [Fact]
    public async Task BuildAsync_DynamicReference_ProducesAnEdgeMarkedDynamic()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS EXEC ('SELECT 1 FROM dbo.Orders');");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>())
            .Returns(Refs(new ObjectRef("dbo", "Orders") { Origin = ReferenceOrigin.Dynamic }));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogEdge edge = Assert.Single(catalog.Edges);
        Assert.True(edge.Dynamic);
    }

    [Fact]
    public async Task BuildAsync_NonAsciiSchemaAndObjectNames_RoundTripThroughTheFileSystem()
    {
        // File names are UTF-8 on both platforms; nothing here may depend on the active code page.
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "vendas", "Pedidos_Coleção", "CREATE TABLE vendas.Pedidos_Coleção (Id INT);");
        CatalogBuilder builder = CreateBuilder();

        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogNode node = Assert.Single(catalog.Nodes);
        Assert.Equal("Pedidos_Coleção", node.Name);
        Assert.Equal("vendas.Pedidos_Coleção", node.QualifiedName);
        Assert.Equal(NodeId("SQLPROD01", "AppDb", "Tables", "vendas", "Pedidos_Coleção"), node.Id);
    }

    /// <summary>Once anything reads the relationship off real DDL, the edge stops being a best-effort finding.</summary>
    [Fact]
    public async Task BuildAsync_EdgeBackedByBothAStaticAndADynamicReference_IsNotMarkedDynamic()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>())
            .Returns(Refs(
                new ObjectRef("dbo", "Orders") { Origin = ReferenceOrigin.Dynamic },
                new ObjectRef("dbo", "Orders")));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        CatalogEdge edge = Assert.Single(catalog.Edges);
        Assert.False(edge.Dynamic);
    }

    /// <summary>
    /// The reported scenario, end to end: a function reaching a remote function through OPENQUERY inside
    /// dynamically-built SQL. MsSqlLineageAnalyzerTests proves the analyzer recovers that reference with
    /// Server = "SQL_A"; this proves what the catalog then does with it - a real hop through the link, and
    /// no orphaned reference anywhere.
    /// </summary>
    [Fact]
    public async Task BuildAsync_DynamicReferenceAcrossALinkedServer_DrawsTheHopAndListsItOnTheLink()
    {
        WriteObjectFile("SQLPROD01", "_ServerLevel", "LinkedServers", null, "SQL_A", LinkedServerDdl("SQL_A", "SQLPROD02", "ServerADb"));
        WriteObjectFile("SQLPROD02", "ServerADb", "Functions", "dbo", "fns_GetOrderId",
            "CREATE FUNCTION dbo.fns_GetOrderId() RETURNS VARCHAR(50) AS BEGIN RETURN ''; END");
        WriteObjectFile("SQLPROD01", "AppDb", "Functions", "dbo", "fns_GetRemoteOrderId",
            "CREATE FUNCTION dbo.fns_GetRemoteOrderId() RETURNS VARCHAR(50) AS BEGIN RETURN ''; END");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("fns_GetRemoteOrderId", StringComparison.Ordinal)), Arg.Any<LineageAnalysisOptions?>())
            .Returns(Refs(
                new ObjectRef("dbo", "fns_GetOrderId") { Server = "SQL_A", Origin = ReferenceOrigin.Dynamic },
                new ObjectRef(null, "sp_executesql")));

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        string callerId = NodeId("SQLPROD01", "AppDb", "Functions", "dbo", "fns_GetRemoteOrderId");
        string linkId = NodeId("SQLPROD01", "_ServerLevel", "LinkedServers", null, "SQL_A");
        string remoteId = NodeId("SQLPROD02", "ServerADb", "Functions", "dbo", "fns_GetOrderId");

        Assert.Contains(catalog.Edges, e => e.From == callerId && e.To == linkId);
        Assert.Contains(catalog.Edges, e => e.From == linkId && e.To == remoteId);
        Assert.Empty(catalog.OrphanedReferences);
        Assert.Single(catalog.SystemReferences);

        CatalogLinkedServerReference reference = Assert.Single(catalog.LinkedServerReferences);
        Assert.Equal(remoteId, reference.To);
        Assert.True(reference.Dynamic);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_objectsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
