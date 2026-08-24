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
        Option<DirectoryInfo> snapshotRootOption = new("--snapshot-root")
        {
            Description = "Root of this run's freshly captured snapshot tree (one JSON file per object, same relative path/id as the object's own .sql file).",
            Required = true,
        };
        Option<DirectoryInfo> historyRootOption = new("--history-root")
        {
            Description = "Root of the accumulating history tree, kept outside config.git.pathPrefix.",
            Required = true,
        };
        Option<int> historyLimitOption = new("--history-limit")
        {
            Description = "Maximum snapshots retained per object; oldest are trimmed first.",
            DefaultValueFactory = _ => 90,
        };

        Command updateCommand = new("update", "Fold this run's metrics snapshots into the accumulating history tree.")
        {
            snapshotRootOption,
            historyRootOption,
            historyLimitOption,
        };

        updateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(MetricsCommand));
            IMetricsHistoryStore metricsHistoryStore = services.GetRequiredService<IMetricsHistoryStore>();

            int updated = await metricsHistoryStore.UpdateAsync(new MetricsHistoryUpdateRequest
            {
                SnapshotRoot = parseResult.GetRequiredValue(snapshotRootOption).FullName,
                HistoryRoot = parseResult.GetRequiredValue(historyRootOption).FullName,
                HistoryLimit = parseResult.GetValue(historyLimitOption),
            }, cancellationToken);

            logger.LogInformation("Metrics history updated for {Count} object(s).", updated);
            return 0;
        });

        return new Command("metrics", "Metrics history commands.") { updateCommand };
    }
}
