namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// That the run itself produced what a pipeline would publish. These are the first assertions to look
/// at when the benchmark goes red: if the CLI did not finish, everything downstream fails for the same
/// reason, and the failure message here carries the CLI's own output.
/// </summary>
[Collection(SampleBenchmarkCollection.Name)]
public sealed class PipelineTests(SampleBenchmarkFixture fleet)
{
    [SampleFleetFact]
    public void PipelineWroteACatalogAndAMetricsHistory()
    {
        Assert.True(File.Exists(SampleFleet.CatalogPath), WithLog($"No catalog at {SampleFleet.CatalogPath}."));
        Assert.True(Directory.Exists(SampleFleet.MetricsRoot), WithLog($"No metrics history at {SampleFleet.MetricsRoot}."));
        Assert.NotEmpty(Directory.EnumerateFiles(SampleFleet.MetricsRoot, "*.json", SearchOption.AllDirectories));
    }

    [SampleFleetFact]
    public void PipelineExtractedObjectsFromBothEngines()
    {
        foreach (string server in fleet.Expectations.Servers)
        {
            string directory = Path.Combine(fleet.OutputRoot, server);
            Assert.True(Directory.Exists(directory), WithLog($"No extracted objects for server '{server}' at {directory}."));
            Assert.NotEmpty(Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories));
        }
    }

    [SampleFleetFact]
    public void CatalogWasBuiltFromThisRun()
    {
        // Not a clock assertion - just that the catalog belongs to the extraction beside it rather
        // than to a stale copy left over from an earlier fleet.
        Assert.NotEmpty(fleet.Catalog.Nodes);
        Assert.All(fleet.Catalog.Nodes, node =>
            Assert.True(
                File.Exists(Path.Combine(fleet.OutputRoot, node.Path.Replace('/', Path.DirectorySeparatorChar))),
                $"Catalog node '{node.Id}' points at '{node.Path}', which is not in {fleet.OutputRoot}."));
    }

    private string WithLog(string message) =>
        fleet.PipelineLog.Length == 0
            ? message
            : $"{message}{Environment.NewLine}{Environment.NewLine}--- syncsql output ---{Environment.NewLine}{fleet.PipelineLog}";
}
