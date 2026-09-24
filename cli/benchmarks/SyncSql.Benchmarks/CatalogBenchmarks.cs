using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using SyncSql.Core.Domain;

namespace SyncSql.Benchmarks;

[SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet invokes GlobalCleanup to dispose the workload.")]
public class CatalogBenchmarks
{
    private BenchmarkWorkload _workload = null!;

    [Params(DatabaseEngine.MsSql, DatabaseEngine.Oracle)]
    public DatabaseEngine Engine { get; set; }

    [Params(16, 64)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup() => _workload = new(NodeCount, Engine);

    [Benchmark]
    public async Task<int> Build()
    {
        var catalog = await _workload.Builder.BuildAsync(_workload.Request, CancellationToken.None);
        BenchmarkWorkload.Validate(catalog, NodeCount);
        return catalog.Edges.Count;
    }

    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
