using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Catalog.Tests;

public sealed class CatalogMemoryTests
{
    [Fact]
    public async Task BuildAsync_ReleasesRawAnalysisBeforeFinishingLineage()
    {
        string root = Directory.CreateTempSubdirectory("syncsql-catalog-memory-").FullName;
        try
        {
            for (int i = 0; i < 8; i++)
            {
                ExtractedObject obj = new()
                {
                    Server = "SQL",
                    Database = "App",
                    Schema = "dbo",
                    Type = "Views",
                    Name = $"V{i}",
                    Engine = DatabaseEngine.MsSql,
                    Ddl = $"SELECT t.Id FROM dbo.V{(i + 1) % 8} t;",
                    Columns = [new ExtractedColumn("Id", "int", null)],
                };
                string path = Path.Combine(root, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, ExtractedObjectFile.Write(obj));
            }

            var analyzer = new LifetimeCheckingAnalyzer();
            var resolver = Substitute.For<ILineageAnalyzerResolver>();
            resolver.Resolve(DatabaseEngine.MsSql).Returns(analyzer);
            var builder = new CatalogBuilder(resolver, Substitute.For<IGitHistoryMiner>(), Substitute.For<IMetricsHistoryStore>(),
                TimeProvider.System, NullLogger<CatalogBuilder>.Instance);

            var catalog = await builder.BuildAsync(new CatalogBuildRequest { ObjectsRoot = root }, CancellationToken.None);

            Assert.Equal(8, analyzer.Calls);
            Assert.Equal(8, catalog.Edges.Count);
            Assert.All(catalog.Edges, edge => Assert.Equal(["Id"], edge.Columns));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class LifetimeCheckingAnalyzer : ILineageAnalyzer
    {
        private readonly List<WeakReference> _analyses = [];
        public DatabaseEngine Engine => DatabaseEngine.MsSql;
        public int Calls => _analyses.Count;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null)
        {
            // Allow the immediately preceding result to remain in a JIT stack slot. Older results
            // must be collectible while the build is still running, independent of heap/GC timing.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.All(_analyses.Take(Math.Max(0, Calls - 1)), reference => Assert.False(reference.IsAlive));

            ObjectRef target = new("dbo", $"V{(Calls + 1) % 8}");
            LineageAnalysisResult result = new()
            {
                ObjectRefs = [target],
                Aliases = new Dictionary<string, ObjectRef> { ["t"] = target },
                ColumnRefs = [.. Enumerable.Range(0, 1024).Select(_ => new ColumnRef("t", "Id"))],
            };
            _analyses.Add(new WeakReference(result));
            return result;
        }
    }
}
