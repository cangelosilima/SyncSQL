using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// Runs the extraction once for the whole benchmark and hands every test the same catalog.
///
/// By default it re-extracts, because that is what the benchmark is measuring; set
/// SYNCSQL_SAMPLES_REUSE=1 to reuse an existing samples/output while iterating on the assertions.
/// When the fleet is switched off, this does nothing at all - it must not throw, because xUnit builds
/// collection fixtures even for collections whose tests are all skipped.
/// </summary>
public sealed class SampleBenchmarkFixture : IAsyncLifetime
{
    private Core.Domain.Catalog? _catalog;
    private SampleExpectations? _expectations;

    /// <summary>Everything the CLI printed, so a failing assertion can show what the run actually did.</summary>
    public string PipelineLog { get; private set; } = string.Empty;

    public string OutputRoot => SampleFleet.OutputRoot;

    public Core.Domain.Catalog Catalog => _catalog
        ?? throw new InvalidOperationException("The catalog was not loaded - the fixture did not initialize.");

    public SampleExpectations Expectations => _expectations
        ?? throw new InvalidOperationException("Expectations were not loaded - the fixture did not initialize.");

    public async Task InitializeAsync()
    {
        if (!SampleFleet.IsEnabled)
        {
            return;
        }

        _expectations = SampleExpectations.Load(SampleFleet.ExpectationsPath);

        if (ShouldExtract())
        {
            SyncSqlPipeline pipeline = new();
            try
            {
                await pipeline.RunAsync(CancellationToken.None);
            }
            finally
            {
                PipelineLog = pipeline.Log;
            }
        }

        _catalog = LoadCatalog();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static bool ShouldExtract() =>
        !SampleFleet.ReuseExistingOutput || !File.Exists(SampleFleet.CatalogPath);

    private static Core.Domain.Catalog LoadCatalog()
    {
        if (!File.Exists(SampleFleet.CatalogPath))
        {
            throw new FileNotFoundException(
                $"No catalog at {SampleFleet.CatalogPath}. The pipeline should have written one - check the run output above.",
                SampleFleet.CatalogPath);
        }

        using FileStream stream = File.OpenRead(SampleFleet.CatalogPath);
        return JsonSerializer.Deserialize<Core.Domain.Catalog>(stream, SyncSqlJsonOptions.Default)
            ?? throw new InvalidOperationException($"{SampleFleet.CatalogPath} deserialized to null.");
    }

    /// <summary>All nodes for one expectation group, keyed the way the group describes them.</summary>
    public IReadOnlyList<CatalogNode> NodesFor(ExpectedGroup group) =>
        [.. Catalog.Nodes.Where(node =>
            string.Equals(node.Server, group.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(node.Database, group.Database, StringComparison.OrdinalIgnoreCase)
            && (group.Schema is null || string.Equals(node.Schema, group.Schema, StringComparison.OrdinalIgnoreCase)))];
}

/// <summary>
/// One collection for every benchmark class, so the extraction happens once and the classes run in
/// sequence rather than fighting over the same output directory.
/// </summary>
[CollectionDefinition(SampleBenchmarkCollection.Name)]
public sealed class SampleBenchmarkCollection : ICollectionFixture<SampleBenchmarkFixture>
{
    public const string Name = "sample-fleet";
}
