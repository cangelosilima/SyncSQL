using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql metrics update` - folds this run's metrics snapshots into the accumulating history tree, mirrors Update-MetricsHistory.ps1. Wired up once SyncSql.Catalog exists.</summary>
internal static class MetricsCommand
{
    public static Command Build(IServiceProvider services)
    {
        Command updateCommand = new("update", "Fold this run's metrics snapshots into the accumulating history tree.");
        updateCommand.SetAction((_, _) =>
        {
            services.GetLogger(nameof(MetricsCommand)).LogError("Not yet implemented - see the SyncSql.Catalog phase.");
            return Task.FromResult(1);
        });

        return new Command("metrics", "Metrics history commands.") { updateCommand };
    }
}
