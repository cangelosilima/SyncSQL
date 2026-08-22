using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Cli.Sync;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Cli.Commands;

/// <summary>
/// `syncsql sync` - the umbrella command: extract every configured server, build catalog.json, fold
/// metrics history, and (unless --skip-git) publish to the target git repository. A direct port of
/// Export-DatabaseObjects.ps1's end-to-end orchestration.
/// </summary>
internal static class SyncCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<FileInfo> configOption = new("--config") { Description = "Path to config/servers.json.", Required = true };
        Option<string?> stagingRootOption = new("--staging-root")
        {
            Description = "Local directory the extraction is written to before being synced into the git checkout. Defaults to a fresh temp directory.",
        };
        Option<string?> metricsSnapshotRootOption = new("--metrics-snapshot-root")
        {
            Description = "Local directory this run's volatile metrics snapshots are written to (separate from --staging-root - never committed as-is, only folded into history at publish time). Defaults to a fresh temp directory. Set this (alongside --staging-root) when a downstream job needs to collect both, e.g. one server's extraction running as its own parallel CI job feeding a later `git publish` step.",
        };
        Option<bool> skipGitOption = new("--skip-git")
        {
            Description = "Run the extraction and leave results under --staging-root without cloning/committing/pushing anything.",
        };
        Option<string[]> serverIncludeOption = new("--server-include") { Description = "Regex override for which configured servers run. Takes precedence over config.serverSelection." };
        Option<string[]> serverExcludeOption = new("--server-exclude") { Description = "Regex override for which configured servers are skipped. Takes precedence over config.serverSelection." };
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
        Option<FileInfo?> dotenvPathOption = new("--dotenv-path")
        {
            Description = "Optional path to write a small KEY=VALUE file with the resolved PATH_PREFIX and GIT_BRANCH (config.git.pathPrefix/.branch, after defaulting). Meant to be picked up by CI as a dotenv artifact report so a downstream job can act on the same path/branch this run publishes to.",
        };

        Command command = new("sync", "Extract, catalog, and publish - the end-to-end pipeline.")
        {
            configOption,
            stagingRootOption,
            metricsSnapshotRootOption,
            skipGitOption,
            serverIncludeOption,
            serverExcludeOption,
            historyLimitOption,
            metricsHistoryLimitOption,
            pushTokenOption,
            dotenvPathOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(SyncCommand));

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

            string? explicitStagingRoot = parseResult.GetValue(stagingRootOption);
            bool stagingRootExplicit = explicitStagingRoot is not null;
            string stagingRoot = explicitStagingRoot ?? Path.Combine(Path.GetTempPath(), $"syncsql-staging-{Guid.NewGuid()}");
            Directory.CreateDirectory(stagingRoot);
            logger.LogInformation("Staging extracted objects under {StagingRoot}", stagingRoot);

            // Separate from stagingRoot on purpose: this only ever holds *this run's* volatile metrics
            // snapshots, never committed as-is - the post-sync hook below folds it into an accumulating
            // history tree outside config.git.pathPrefix.
            string? explicitMetricsRoot = parseResult.GetValue(metricsSnapshotRootOption);
            bool metricsRootExplicit = explicitMetricsRoot is not null;
            string metricsRoot = explicitMetricsRoot ?? Path.Combine(Path.GetTempPath(), $"syncsql-metrics-{Guid.NewGuid()}");
            Directory.CreateDirectory(metricsRoot);

            string[] includeOverride = parseResult.GetValue(serverIncludeOption) ?? [];
            string[] excludeOverride = parseResult.GetValue(serverExcludeOption) ?? [];
            NameFilter serverSelection = config.ServerSelection with
            {
                Include = includeOverride.Length > 0 ? includeOverride : config.ServerSelection.Include,
                Exclude = excludeOverride.Length > 0 ? excludeOverride : config.ServerSelection.Exclude,
            };

            ICredentialProvider credentialProvider = services.GetRequiredService<ICredentialProvider>();
            IDatabaseObjectExtractorResolver extractorResolver = services.GetRequiredService<IDatabaseObjectExtractorResolver>();

            List<string> summaryLines = [];
            List<string> failedServers = [];
            int totalFiles = 0;

            foreach (ServerConfig server in config.Servers)
            {
                if (!serverSelection.IsAllowed(server.Name))
                {
                    logger.LogInformation("Skipping '{Server}' (excluded by server selection filter)", server.Name);
                    continue;
                }

                EffectiveFilters filters = EffectiveFilters.Resolve(config.Defaults, server);
                if (filters.ObjectTypes.Count == 0)
                {
                    logger.LogWarning("Skipping '{Server}': no objectTypes configured (defaults + server override both empty).", server.Name);
                    continue;
                }

                DatabaseCredentials credentials;
                try
                {
                    credentials = credentialProvider.Resolve(server.CredentialsVariablePrefix);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError("Skipping '{Server}': {Message}", server.Name, ex.Message);
                    failedServers.Add(server.Name);
                    continue;
                }

                try
                {
                    IDatabaseObjectExtractor extractor = extractorResolver.Resolve(server.Type);
                    ExtractionOutcome outcome = await extractor.ExtractAsync(
                        server, filters, new ExtractionOptions { Credentials = credentials }, cancellationToken);

                    await ExtractionOutputWriter.WriteAsync(outcome, stagingRoot, metricsRoot, cancellationToken);

                    totalFiles += outcome.Objects.Count;
                    summaryLines.Add($"- {server.Name} ({server.Type.ToConfigString()}): {outcome.Objects.Count} object file(s)");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError("Extraction failed for '{Server}': {Message}", server.Name, ex.Message);
                    failedServers.Add(server.Name);
                    summaryLines.Add($"- {server.Name} ({server.Type.ToConfigString()}): FAILED - {ex.Message}");
                }
            }

            logger.LogInformation(
                "Extraction complete: {TotalFiles} object file(s) across {ServerCount} server(s); {FailureCount} failure(s).",
                totalFiles, config.Servers.Count - failedServers.Count, failedServers.Count);

            bool skipGit = parseResult.GetValue(skipGitOption);
            int publishExitCode = 0;
            if (skipGit)
            {
                logger.LogInformation("--skip-git set; leaving extracted files at {StagingRoot}", stagingRoot);
                logger.LogInformation("--skip-git set; leaving this run's metrics snapshots at {MetricsRoot} (no history tree to accumulate them into without a git checkout)", metricsRoot);
            }
            else
            {
                string? pushToken = parseResult.GetValue(pushTokenOption)
                    ?? Environment.GetEnvironmentVariable("CI_JOB_Maintainer_Token")
                    ?? Environment.GetEnvironmentVariable("GIT_PUSH_TOKEN");

                publishExitCode = await GitPublishOrchestrator.PublishAsync(
                    services,
                    config,
                    stagingRoot,
                    stagingRootExplicit,
                    metricsRoot,
                    metricsRootExplicit,
                    pushToken,
                    parseResult.GetValue(historyLimitOption),
                    parseResult.GetValue(metricsHistoryLimitOption),
                    string.Join('\n', summaryLines),
                    logger,
                    cancellationToken);
            }

            if (failedServers.Count > 0)
            {
                logger.LogError("Failed server(s): {Servers}", string.Join(", ", failedServers));
                return 1;
            }

            return publishExitCode;
        });

        return command;
    }
}
