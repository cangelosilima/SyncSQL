using System.CommandLine;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Cli.Sync;
using SyncSql.Core.Configuration;

namespace SyncSql.Cli.Commands;

/// <summary>
/// `syncsql git publish` - publishes an already-populated extracted-objects tree: clone, replace
/// config.git.pathPrefix, fold metrics, rebuild catalog.json, commit, push. No extraction of its own -
/// pairs with one or more `sync --skip-git` runs that populated --staging-root (and, optionally,
/// --metrics-snapshot-root) beforehand, e.g. several servers extracted as independent parallel CI jobs
/// whose merged artifacts this command then publishes in one commit.
/// </summary>
internal static class GitCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<FileInfo> configOption = new("--config") { Description = "Path to config/servers.json.", Required = true };
        Option<DirectoryInfo> stagingRootOption = new("--staging-root")
        {
            Description = "Pre-populated extracted-objects tree to publish (e.g. the merged output of one or more `sync --skip-git` runs).",
            Required = true,
        };
        Option<DirectoryInfo?> metricsSnapshotRootOption = new("--metrics-snapshot-root")
        {
            Description = "This run's metrics snapshots (e.g. merged from the same parallel extraction jobs as --staging-root). Omit to publish without folding in new metrics - catalog.json is still built from whatever metrics history the target repo's own metrics/ tree already has.",
        };
        Option<int> historyLimitOption = new("--history-limit")
        {
            Description = "How many commits to clone (so catalog.json can be built/versioned in the same push) and mine for catalog history.",
            DefaultValueFactory = _ => 250,
        };
        Option<int> metricsHistoryLimitOption = new("--metrics-history-limit")
        {
            Description = "Maximum number of daily metrics snapshots retained per table.",
            DefaultValueFactory = _ => 90,
        };
        Option<string?> pushTokenOption = new("--push-token")
        {
            Description = "Token used to push to the target git repository. Defaults to CI_JOB_Maintainer_Token, falling back to GIT_PUSH_TOKEN.",
        };
        Option<string?> summaryOption = new("--summary")
        {
            Description = "Extra text appended to the commit message, e.g. a per-server extraction summary collected from the upstream extraction jobs.",
        };
        Option<FileInfo?> dotenvPathOption = new("--dotenv-path")
        {
            Description = "Optional path to write a small KEY=VALUE file with the resolved PATH_PREFIX and GIT_BRANCH (config.git.pathPrefix/.branch, after defaulting). Meant to be picked up by CI as a dotenv artifact report so a downstream job can act on the same path/branch this run publishes to.",
        };

        Command publishCommand = new("publish", "Publish a pre-populated extracted-objects tree - no extraction of its own.")
        {
            configOption,
            stagingRootOption,
            metricsSnapshotRootOption,
            historyLimitOption,
            metricsHistoryLimitOption,
            pushTokenOption,
            summaryOption,
            dotenvPathOption,
        };

        publishCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(GitCommand));

            FileInfo configFile = parseResult.GetRequiredValue(configOption);
            logger.LogInformation("Loading config from {Path}", configFile.FullName);
            SyncSqlConfig config;
            try
            {
                config = await SyncSqlConfigLoader.LoadAsync(configFile.FullName, cancellationToken);
            }
            catch (ConfigValidationException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }

            FileInfo? dotenvFile = parseResult.GetValue(dotenvPathOption);
            if (dotenvFile is not null)
            {
                ResolvedGitConfig gitDefaults = config.Git.Resolved();
                await File.WriteAllTextAsync(dotenvFile.FullName, $"PATH_PREFIX={gitDefaults.PathPrefix}\nGIT_BRANCH={gitDefaults.Branch}\n", cancellationToken);
                logger.LogInformation("Wrote {Path}", dotenvFile.FullName);
            }

            string? pushToken = parseResult.GetValue(pushTokenOption)
                ?? Environment.GetEnvironmentVariable("CI_JOB_Maintainer_Token")
                ?? Environment.GetEnvironmentVariable("GIT_PUSH_TOKEN");

            return await GitPublishOrchestrator.PublishAsync(
                services,
                config,
                parseResult.GetRequiredValue(stagingRootOption).FullName,
                stagingRootExplicit: true,
                parseResult.GetValue(metricsSnapshotRootOption)?.FullName,
                metricsRootExplicit: true,
                pushToken,
                parseResult.GetValue(historyLimitOption),
                parseResult.GetValue(metricsHistoryLimitOption),
                parseResult.GetValue(summaryOption) ?? string.Empty,
                logger,
                cancellationToken);
        });

        return new Command("git", "Git publish commands.") { publishCommand };
    }
}
