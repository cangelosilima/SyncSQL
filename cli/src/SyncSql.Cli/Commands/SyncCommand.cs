using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>
/// `syncsql sync` - the umbrella command: extract every configured server, build catalog.json, fold
/// metrics history, and (unless --skip-git) publish to the target git repository. Mirrors
/// Export-DatabaseObjects.ps1's end-to-end orchestration. Wired up in full once the extraction/catalog/git
/// backends exist (see the solution's execution-phases plan).
/// </summary>
internal static class SyncCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<FileInfo> configOption = new("--config") { Description = "Path to config/servers.json.", Required = true };

        Command command = new("sync", "Extract, catalog, and publish - the end-to-end pipeline.")
        {
            configOption,
        };

        command.SetAction((_, _) =>
        {
            services.GetLogger(nameof(SyncCommand)).LogError("Not yet implemented - see the extraction/catalog/git backend phases.");
            return Task.FromResult(1);
        });

        return command;
    }
}
