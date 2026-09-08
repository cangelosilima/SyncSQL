using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Core.Abstractions;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql metrics update` - folds this run's metrics snapshots into the accumulating history tree, mirrors Update-MetricsHistory.ps1.</summary>
internal static class MetricsCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string?> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string?> snapshotRootOption = new("--snapshot-root")
        {
            Description = $"Root of this run's freshly captured snapshot tree (one JSON file per object, same relative path/id as the object's own .sql file). Default: <output-root>/{SyncSqlPaths.MetricsSnapshotDirectoryName}, i.e. `sync`'s --metrics-snapshot-root.",
        };
        Option<string?> historyRootOption = new("--history-root")
        {
            Description = $"Root of the accumulating history tree, kept outside config.git.pathPrefix. Default: <output-root>/{SyncSqlPaths.MetricsHistoryDirectoryName}.",
        };
        Option<int> historyLimitOption = new("--history-limit")
        {
            Description = "Maximum snapshots retained per object; oldest are trimmed first.",
            DefaultValueFactory = _ => 90,
        };

        Command updateCommand = new("update", "Fold this run's metrics snapshots into the accumulating history tree.")
        {
            outputRootOption,
            snapshotRootOption,
            historyRootOption,
            historyLimitOption,
        };

        updateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(MetricsCommand));
            IMetricsHistoryStore metricsHistoryStore = services.GetRequiredService<IMetricsHistoryStore>();

            try
            {
                string? explicitSnapshotRoot = parseResult.GetValue(snapshotRootOption);
                string? inferredRoot = string.IsNullOrWhiteSpace(explicitSnapshotRoot) ? null :
                    Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(explicitSnapshotRoot))) ?? Path.GetFullPath(explicitSnapshotRoot);
                string[] outputRoots = SyncSqlPaths.ReadOutputRoots(parseResult.GetValue(outputRootOption), inferredRoot);
                if (outputRoots.Length > 1 && !string.IsNullOrWhiteSpace(parseResult.GetValue(historyRootOption)))
                {
                    logger.LogError("Multiple engine roots found. Select --output-root or --snapshot-root when supplying a single --history-root.");
                    return 1;
                }
                foreach (string outputRoot in outputRoots)
                {
                    string snapshotRoot = SyncSqlPaths.Resolve(parseResult.GetValue(snapshotRootOption), outputRoot, SyncSqlPaths.MetricsSnapshotDirectoryName);
                    string historyRoot = SyncSqlPaths.Resolve(parseResult.GetValue(historyRootOption), outputRoot, SyncSqlPaths.MetricsHistoryDirectoryName);

                    int updated = await metricsHistoryStore.UpdateAsync(new MetricsHistoryUpdateRequest
                    {
                        SnapshotRoot = snapshotRoot,
                        HistoryRoot = historyRoot,
                        HistoryLimit = parseResult.GetValue(historyLimitOption),
                    }, cancellationToken);

                    logger.LogInformation("Metrics history updated for {Count} object(s).", updated);
                }
                return 0;
            }
            catch (DirectoryNotFoundException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }
        });

        return new Command("metrics", "Metrics history commands.") { updateCommand };
    }
}
