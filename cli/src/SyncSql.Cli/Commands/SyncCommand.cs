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
/// `syncsql sync` - extracts every configured (and selected) server, writing each object as its own
/// `.sql` file and each table's metrics as its own snapshot file. Purely local: no git operations here -
/// cloning, staging into config.git.pathPrefix, folding metrics history, rebuilding catalog.json, and
/// pushing are all orchestrated directly by the CI pipeline (see the root .gitlab-ci.yml), which calls
/// `syncsql metrics update` and `syncsql catalog build` for the parts that aren't git itself.
/// </summary>
internal static class SyncCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<FileInfo> configOption = new("--config") { Description = "Path to config/servers.json.", Required = true };
        Option<string?> stagingRootOption = new("--staging-root")
        {
            Description = "Local directory each extracted object is written to. Defaults to a fresh temp directory.",
        };
        Option<string?> metricsSnapshotRootOption = new("--metrics-snapshot-root")
        {
            Description = "Local directory this run's volatile metrics snapshots are written to (separate from --staging-root - one JSON file per table, meant to be folded into history later via `syncsql metrics update`). Defaults to a fresh temp directory.",
        };
        Option<string[]> serverIncludeOption = new("--server-include") { Description = "Regex override for which configured servers run. Takes precedence over config.serverSelection." };
        Option<string[]> serverExcludeOption = new("--server-exclude") { Description = "Regex override for which configured servers are skipped. Takes precedence over config.serverSelection." };

        Command command = new("sync", "Extract every configured server's database objects and metrics snapshots.")
        {
            configOption,
            stagingRootOption,
            metricsSnapshotRootOption,
            serverIncludeOption,
            serverExcludeOption,
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

            string stagingRoot = parseResult.GetValue(stagingRootOption) ?? Path.Combine(Path.GetTempPath(), $"syncsql-staging-{Guid.NewGuid()}");
            Directory.CreateDirectory(stagingRoot);
            logger.LogInformation("Staging extracted objects under {StagingRoot}", stagingRoot);

            string metricsRoot = parseResult.GetValue(metricsSnapshotRootOption) ?? Path.Combine(Path.GetTempPath(), $"syncsql-metrics-{Guid.NewGuid()}");
            Directory.CreateDirectory(metricsRoot);
            logger.LogInformation("Staging metrics snapshots under {MetricsRoot}", metricsRoot);

            string[] includeOverride = parseResult.GetValue(serverIncludeOption) ?? [];
            string[] excludeOverride = parseResult.GetValue(serverExcludeOption) ?? [];
            NameFilter serverSelection = config.ServerSelection with
            {
                Include = includeOverride.Length > 0 ? includeOverride : config.ServerSelection.Include,
                Exclude = excludeOverride.Length > 0 ? excludeOverride : config.ServerSelection.Exclude,
            };

            ICredentialProvider credentialProvider = services.GetRequiredService<ICredentialProvider>();
            IDatabaseObjectExtractorResolver extractorResolver = services.GetRequiredService<IDatabaseObjectExtractorResolver>();

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
                    logger.LogInformation("- {Server} ({Engine}): {Count} object file(s)", server.Name, server.Type.ToConfigString(), outcome.Objects.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError("Extraction failed for '{Server}': {Message}", server.Name, ex.Message);
                    failedServers.Add(server.Name);
                }
            }

            logger.LogInformation(
                "Extraction complete: {TotalFiles} object file(s) across {ServerCount} server(s); {FailureCount} failure(s).",
                totalFiles, config.Servers.Count - failedServers.Count, failedServers.Count);

            if (failedServers.Count > 0)
            {
                logger.LogError("Failed server(s): {Servers}", string.Join(", ", failedServers));
                return 1;
            }

            return 0;
        });

        return command;
    }
}
