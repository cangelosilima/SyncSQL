using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Catalog;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;
using SyncSql.Lineage.MsSql;
using SyncSql.Lineage.Oracle;

namespace SyncSql.Benchmarks;

internal sealed class BenchmarkWorkload : IDisposable, ILineageAnalyzerResolver, IGitHistoryMiner, IMetricsHistoryStore
{
    private readonly ILineageAnalyzer _analyzer;
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-benchmark-").FullName;

    public BenchmarkWorkload(int nodeCount, DatabaseEngine engine, ILineageAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? CreateAnalyzer(engine);
        try
        {
            for (int i = 0; i < nodeCount; i++)
            {
                string name = "V" + i.ToString("D3", CultureInfo.InvariantCulture);
                ExtractedObject obj = new()
                {
                    Server = "SERVER",
                    Database = "App",
                    Schema = "APP",
                    Type = i == 0 ? "Tables" : "Views",
                    Name = name,
                    Engine = engine,
                    Ddl = i == 0 ? "CREATE TABLE APP.V000 (Id INT, ParentId INT);"
                        : $"CREATE VIEW APP.{name} AS {SelectSql(i)}",
                    Columns = [new ExtractedColumn("Id", "int", null), new ExtractedColumn("ParentId", "int", null)],
                };
                string path = Path.Combine(_root, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, ExtractedObjectFile.Write(obj));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        Builder = new(this, this, this, TimeProvider.System, NullLogger<CatalogBuilder>.Instance);
        Request = new() { ObjectsRoot = _root };
    }

    public CatalogBuilder Builder { get; }
    public CatalogBuildRequest Request { get; }

    public static ILineageAnalyzer CreateAnalyzer(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.MsSql => new MsSqlLineageAnalyzer(NullLogger<MsSqlLineageAnalyzer>.Instance),
        DatabaseEngine.Oracle => new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    public static string SelectSql(int i) => (i % 3) switch
    {
        0 => FormattableString.Invariant($"SELECT t.Id FROM APP.V000 t WHERE t.Id > {i};"),
        1 => "SELECT t.Id, p.ParentId FROM APP.V000 t JOIN APP.V000 p ON p.Id = t.ParentId;",
        _ => "SELECT t.Id FROM APP.V000 t WHERE EXISTS (SELECT 1 FROM APP.V000 p WHERE p.Id = t.ParentId);",
    };

    public static void Validate(Core.Domain.Catalog catalog, int nodeCount)
    {
        if (catalog.Nodes.Count != nodeCount || catalog.Edges.Count != nodeCount - 1
            || catalog.Edges.Any(edge => !edge.Columns.Contains("Id", StringComparer.OrdinalIgnoreCase))
            || catalog.OrphanedReferences.Count != 0)
        {
            throw new InvalidOperationException("Benchmark catalog lost nodes, dependencies, or column tags.");
        }
    }

    public ILineageAnalyzer Resolve(DatabaseEngine engine) => _analyzer;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    // These scenarios intentionally have no metrics/history. An unexpected call is a fixture error.
    public Task<GitHistoryMiningResult> MineAsync(GitHistoryMiningRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("History is disabled in this benchmark.");
    public Task<int> UpdateAsync(MetricsHistoryUpdateRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Metrics are disabled in this benchmark.");
    public Task<IReadOnlyList<MetricsSnapshot>> LoadHistoryAsync(string historyRoot, string objectId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Metrics are disabled in this benchmark.");
}
