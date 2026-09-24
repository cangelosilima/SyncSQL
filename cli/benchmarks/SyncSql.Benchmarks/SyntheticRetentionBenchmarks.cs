using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Benchmarks;

/// <summary>
/// The former .cache probe, hosted by BenchmarkDotNet. Forced collections deliberately measure
/// retained analysis between objects; timings include that diagnostic cost, unlike CatalogBenchmarks.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet invokes GlobalCleanup to dispose the workload.")]
public class SyntheticRetentionBenchmarks
{
    private BenchmarkWorkload _workload = null!;
    private RetentionAnalyzer _analyzer = null!;
    private long _peakRetainedBytes;
    private int _completedBuilds;

    [Params(64, 256)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new(NodeCount);
        _workload = new(NodeCount, DatabaseEngine.MsSql, _analyzer);
    }

    [Benchmark]
    public async Task<int> BuildAndSampleRetention()
    {
        _analyzer.Reset();
        var catalog = await _workload.Builder.BuildAsync(_workload.Request, CancellationToken.None);
        BenchmarkWorkload.Validate(catalog, NodeCount);
        if (_analyzer.Calls != NodeCount)
        {
            throw new InvalidOperationException("Synthetic analyzer did not process every object.");
        }
        _peakRetainedBytes = Math.Max(_peakRetainedBytes, _analyzer.PeakRetainedBytes);
        _completedBuilds++;
        return catalog.Edges.Count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _workload.Dispose();
        string root = Environment.GetEnvironmentVariable(Program.ArtifactsVariable)
            ?? throw new InvalidOperationException("Benchmark artifacts directory was not configured.");
        string directory = Path.Combine(root, "retention");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"synthetic-{NodeCount.ToString(CultureInfo.InvariantCulture)}.json"),
            JsonSerializer.Serialize(new { NodeCount, ColumnReferencesPerObject = 8193, PeakRetainedAnalysisBytes = _peakRetainedBytes, CompletedBuilds = _completedBuilds }));
    }

    private sealed class RetentionAnalyzer(int nodeCount) : ILineageAnalyzer
    {
        private long _initialBytes;
        public DatabaseEngine Engine => DatabaseEngine.MsSql;
        public int Calls { get; private set; }
        public long PeakRetainedBytes { get; private set; }

        public void Reset()
        {
            Calls = 0;
            PeakRetainedBytes = 0;
            _initialBytes = 0;
        }

        public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null)
        {
            if (Calls % 32 == 0 || Calls == nodeCount - 1)
            {
                long liveBytes = GC.GetTotalMemory(forceFullCollection: true);
                if (Calls == 0)
                {
                    _initialBytes = liveBytes;
                }
                PeakRetainedBytes = Math.Max(PeakRetainedBytes, liveBytes - _initialBytes);
            }
            Calls++;
            ObjectRef target = new("APP", "V000");
            return new LineageAnalysisResult
            {
                ObjectRefs = [target],
                Aliases = new Dictionary<string, ObjectRef> { ["t"] = target },
                ColumnRefs = [new ColumnRef("t", "Id"), .. Enumerable.Range(0, 8192)
                    .Select(i => new ColumnRef("t", "UnknownColumn" + i.ToString(CultureInfo.InvariantCulture)))],
            };
        }
    }
}
