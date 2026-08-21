using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Catalog.Tests;

public sealed class CatalogBuilderTests : IDisposable
{
    private readonly string _objectsRoot = Directory.CreateTempSubdirectory("syncsql-objects-").FullName;
    private readonly ILineageAnalyzerResolver _lineageAnalyzerResolver = Substitute.For<ILineageAnalyzerResolver>();
    private readonly ILineageAnalyzer _mssqlAnalyzer = Substitute.For<ILineageAnalyzer>();
    private readonly IGitHistoryMiner _gitHistoryMiner = Substitute.For<IGitHistoryMiner>();
    private readonly IMetricsHistoryStore _metricsHistoryStore = Substitute.For<IMetricsHistoryStore>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-01T12:00:00Z");

    public CatalogBuilderTests()
    {
        _mssqlAnalyzer.Engine.Returns(DatabaseEngine.MsSql);
        _mssqlAnalyzer.Analyze(Arg.Any<string>()).Returns(LineageAnalysisResult.Empty);
        _lineageAnalyzerResolver.Resolve(DatabaseEngine.MsSql).Returns(_mssqlAnalyzer);
        _clock.UtcNow.Returns(FixedNow);
    }

    private CatalogBuilder CreateBuilder() => new(
        _lineageAnalyzerResolver,
        _gitHistoryMiner,
        _metricsHistoryStore,
        _clock,
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

    [Fact]
    public async Task BuildAsync_EmptyTree_ProducesEmptyCatalogAtGeneratedAtFromClock()
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
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("FROM dbo.Orders", StringComparison.Ordinal))).Returns(referencesOrders);

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
    public async Task BuildAsync_UnresolvableReference_ProducesNoEdge()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.NoSuchTable;");
        _mssqlAnalyzer.Analyze(Arg.Any<string>()).Returns(new LineageAnalysisResult
        {
            ObjectRefs = [new ObjectRef("dbo", "NoSuchTable")],
            Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
            ColumnRefs = [],
        });

        CatalogBuilder builder = CreateBuilder();
        Core.Domain.Catalog catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = _objectsRoot }, CancellationToken.None);

        Assert.Empty(catalog.Edges);
    }

    [Fact]
    public async Task BuildAsync_DuplicateObjectRefs_ProduceOnlyOneEdge()
    {
        WriteObjectFile("SQLPROD01", "AppDb", "Tables", "dbo", "Orders", "CREATE TABLE dbo.Orders (Id INT);");
        WriteObjectFile("SQLPROD01", "AppDb", "StoredProcedures", "dbo", "GetOrder",
            "CREATE PROCEDURE dbo.GetOrder AS SELECT 1 FROM dbo.Orders; SELECT 2 FROM dbo.Orders;");
        _mssqlAnalyzer.Analyze(Arg.Is<string>(s => s.Contains("GetOrder", StringComparison.Ordinal))).Returns(new LineageAnalysisResult
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
        _mssqlAnalyzer.Analyze(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("lineage should not run for a node with no engine"));

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
