using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Perfolizer.Mathematics.OutlierDetection;

namespace SyncSql.Benchmarks;

internal static class Program
{
    internal const string ArtifactsVariable = "SYNCSQL_BENCHMARK_ARTIFACTS";

    private static int Main(string[] args)
    {
        string artifacts = Path.GetFullPath(Environment.GetEnvironmentVariable(ArtifactsVariable) ?? "BenchmarkDotNet.Artifacts");
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(artifacts)
            .WithBuildTimeout(TimeSpan.FromMinutes(5))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.Full)
            .AddJob(Job.ShortRun.WithId("Quality")
                .WithWarmupCount(1).WithInvocationCount(1).WithUnrollFactor(1).WithOutlierMode(OutlierMode.DontRemove)
                .WithEnvironmentVariable(ArtifactsVariable, artifacts));
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
            .Run(args.Length == 0 ? ["--filter", "*"] : args, config).ToArray();

        // BenchmarkDotNet can report a failed child without throwing in the host.
        return summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors
            || summary.Reports.Length == 0 || summary.Reports.Any(report => !report.Success)) ? 1 : 0;
    }
}
