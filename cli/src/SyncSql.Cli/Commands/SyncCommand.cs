using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Cli.Sync;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Credentials;
using SyncSql.Core.Domain;

namespace SyncSql.Cli.Commands;

/// <summary>
/// `syncsql sync` - extracts every configured (and selected) server, writing each object as its own
/// `.sql` file and each table's metrics as its own snapshot file. Purely local: no git operations here -
/// cloning, staging into config.git.pathPrefix, folding metrics history, rebuilding catalog.json, and
/// pushing are all orchestrated by the calling pipeline (scripts/Publish-SyncSqlObjects.ps1, driven by
/// .gitlab/ci/sync.yml - see .gitlab/README.md), which calls `syncsql metrics update` and
/// `syncsql catalog build` for the parts that aren't git itself.
///
/// Every input is a parameter: credentials via --db-user/--db-password/--credentials-file (environment
/// variables remain the last-resort fallback, so an existing CI setup keeps working), and output paths
/// via --output-root/--staging-root/--metrics-snapshot-root.
/// </summary>
internal static class SyncCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string> configOption = new("--config")
        {
            Description = "Path to config/servers.json, relative to the current directory unless absolute.",
            DefaultValueFactory = _ => SyncSqlPaths.DefaultConfigPath,
        };
        Option<string> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string?> stagingRootOption = new("--staging-root")
        {
            Description = $"Directory each extracted object is written to. Default: <output-root>/{SyncSqlPaths.ObjectsDirectoryName}.",
        };
        Option<string?> metricsSnapshotRootOption = new("--metrics-snapshot-root")
        {
            Description = $"Directory this run's volatile metrics snapshots are written to (separate from --staging-root - one JSON file per table, folded into history later via `syncsql metrics update`). Default: <output-root>/{SyncSqlPaths.MetricsSnapshotDirectoryName}.",
        };
        Option<string[]> dbUserOption = new("--db-user")
        {
            Description = "Database username for one server, as PREFIX=value where PREFIX is that server's credentialsVariablePrefix. Repeatable. Takes precedence over --credentials-file and the environment.",
        };
        Option<string[]> dbPasswordOption = new("--db-password")
        {
            Description = "Database password for one server, as PREFIX=value. Repeatable. Note that command-line arguments are visible to other processes on the host - prefer --credentials-file where that matters.",
        };
        Option<string?> credentialsFileOption = new("--credentials-file")
        {
            Description = "JSON file of credentials keyed by credentialsVariablePrefix: { \"PREFIX\": { \"user\": \"...\", \"password\": \"...\" } }. Used for any half not passed as a parameter.",
        };
        Option<string[]> serverIncludeOption = new("--server-include") { Description = "Regex override for which configured servers run. Takes precedence over config.serverSelection." };
        Option<string[]> serverExcludeOption = new("--server-exclude") { Description = "Regex override for which configured servers are skipped. Takes precedence over config.serverSelection." };

        Command command = new("sync", "Extract every configured server's database objects and metrics snapshots.")
        {
            configOption,
            outputRootOption,
            stagingRootOption,
            metricsSnapshotRootOption,
            dbUserOption,
            dbPasswordOption,
            credentialsFileOption,
            serverIncludeOption,
            serverExcludeOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(SyncCommand));

            string configPath = Path.GetFullPath(parseResult.GetValue(configOption) ?? SyncSqlPaths.DefaultConfigPath);
            logger.LogInformation("Loading config from {Path}", configPath);
            SyncSqlConfig config;
            try
            {
                config = await SyncSqlConfigLoader.LoadAsync(configPath, cancellationToken);
            }
            catch (ConfigValidationException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }

            string outputRoot = parseResult.GetValue(outputRootOption) ?? SyncSqlPaths.DefaultOutputRoot;
            string stagingRoot = SyncSqlPaths.Resolve(parseResult.GetValue(stagingRootOption), outputRoot, SyncSqlPaths.ObjectsDirectoryName);
            Directory.CreateDirectory(stagingRoot);
            logger.LogInformation("Staging extracted objects under {StagingRoot}", stagingRoot);

            string metricsRoot = SyncSqlPaths.Resolve(parseResult.GetValue(metricsSnapshotRootOption), outputRoot, SyncSqlPaths.MetricsSnapshotDirectoryName);
            Directory.CreateDirectory(metricsRoot);
            logger.LogInformation("Staging metrics snapshots under {MetricsRoot}", metricsRoot);

            ICredentialProvider credentialProvider;
            try
            {
                credentialProvider = await BuildCredentialProviderAsync(services, parseResult, dbUserOption, dbPasswordOption, credentialsFileOption, cancellationToken);
            }
            catch (CredentialParseException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }

            string[] includeOverride = parseResult.GetValue(serverIncludeOption) ?? [];
            string[] excludeOverride = parseResult.GetValue(serverExcludeOption) ?? [];
            NameFilter serverSelection = config.ServerSelection with
            {
                Include = includeOverride.Length > 0 ? includeOverride : config.ServerSelection.Include,
                Exclude = excludeOverride.Length > 0 ? excludeOverride : config.ServerSelection.Exclude,
            };

            IDatabaseObjectExtractorResolver extractorResolver = services.GetRequiredService<IDatabaseObjectExtractorResolver>();

            LinkedServerDiscoveryConfig discovery = config.Discovery.LinkedServers;
            bool followLinkedServers = discovery.Enabled && discovery.MaxDepth > 0;
            if (followLinkedServers)
            {
                logger.LogInformation(
                    "Linked-server discovery is on (maxDepth {MaxDepth}): servers reached through a link are extracted with the same credentials as the server that declared it{CatalogNote}.",
                    discovery.MaxDepth,
                    discovery.RestrictToLinkedCatalog ? ", limited to the database the link pins" : string.Empty);
            }

            List<string> failedServers = [];
            // A server nobody configured is a lead this run chose to chase; failing to reach one says
            // something about the fleet, not about this run's job, so it's reported without failing it.
            List<string> failedDiscoveredServers = [];
            int attemptedServers = 0;
            int totalFiles = 0;
            int discoveredServers = 0;

            // Configured servers first; anything reached by following their linked servers is appended
            // as its own round, so a link found at depth N is extracted at depth N+1 and can in turn be
            // followed (up to discovery.linkedServers.maxDepth).
            List<ServerConfig> knownServers = [.. config.Servers];
            Queue<(ServerConfig Server, int Depth)> pending = new(config.Servers.Select(server => (server, 0)));

            while (pending.Count > 0)
            {
                (ServerConfig server, int depth) = pending.Dequeue();

                if (depth == 0 && !serverSelection.IsAllowed(server.Name))
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

                attemptedServers++;
                DatabaseCredentials credentials;
                try
                {
                    credentials = credentialProvider.Resolve(server.CredentialsVariablePrefix);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError("Skipping '{Server}': {Message}", server.Name, ex.Message);
                    (depth == 0 ? failedServers : failedDiscoveredServers).Add(server.Name);
                    continue;
                }

                bool discoverHere = followLinkedServers && depth < discovery.MaxDepth && server.Type == DatabaseEngine.MsSql;

                try
                {
                    IDatabaseObjectExtractor extractor = extractorResolver.Resolve(server.Type);
                    ExtractionOutcome outcome = await extractor.ExtractAsync(
                        server, filters, new ExtractionOptions { Credentials = credentials, DiscoverLinkedServers = discoverHere }, cancellationToken);

                    await ExtractionOutputWriter.WriteAsync(outcome, stagingRoot, metricsRoot, cancellationToken);

                    totalFiles += outcome.Objects.Count;
                    logger.LogInformation("- {Server} ({Engine}): {Count} object file(s)", server.Name, server.Type.ToConfigString(), outcome.Objects.Count);

                    if (discoverHere)
                    {
                        discoveredServers += QueueLinkedServers(
                            logger, server, credentials.Username, outcome.DiscoveredLinkedServers, discovery, depth, knownServers, pending);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (depth == 0)
                    {
                        logger.LogError("Extraction failed for '{Server}': {Message}", server.Name, ex.Message);
                        failedServers.Add(server.Name);
                    }
                    else
                    {
                        logger.LogWarning("Extraction failed for discovered server '{Server}': {Message}", server.Name, ex.Message);
                        failedDiscoveredServers.Add(server.Name);
                    }
                }
            }

            logger.LogInformation(
                "Extraction complete: {TotalFiles} object file(s) across {ServerCount} server(s) ({DiscoveredCount} reached through a linked server); {FailureCount} failure(s).",
                totalFiles, attemptedServers - failedServers.Count - failedDiscoveredServers.Count, discoveredServers, failedServers.Count);

            if (failedDiscoveredServers.Count > 0)
            {
                logger.LogWarning(
                    "Discovered server(s) that couldn't be extracted (not counted as a run failure): {Servers}",
                    string.Join(", ", failedDiscoveredServers));
            }

            if (failedServers.Count > 0)
            {
                logger.LogError("Failed server(s): {Servers}", string.Join(", ", failedServers));
                return 1;
            }

            return 0;
        });

        return command;
    }

    /// <summary>
    /// Turns the linked servers one extraction reported into the next round of work, logging both what
    /// gets followed and what deliberately doesn't - a link skipped for using a different remote login
    /// is a fact about the fleet worth seeing, not a silent no-op.
    /// </summary>
    private static int QueueLinkedServers(
        ILogger logger,
        ServerConfig parent,
        string parentUsername,
        IReadOnlyList<DiscoveredLinkedServer> discovered,
        LinkedServerDiscoveryConfig discovery,
        int depth,
        List<ServerConfig> knownServers,
        Queue<(ServerConfig Server, int Depth)> pending)
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(parent, parentUsername, discovered, discovery, knownServers);

        foreach (SkippedLinkedServer skipped in plan.Skipped)
        {
            logger.LogInformation("  Not following linked server '{Link}' on '{Server}': {Reason}", skipped.LinkName, parent.Name, skipped.Reason);
        }

        foreach (LinkedServerFollowUp followUp in plan.FollowUps)
        {
            logger.LogInformation(
                "  Following linked server '{Link}' on '{Server}' -> '{Target}' ({Host}{Database}), same credentials",
                followUp.LinkName,
                parent.Name,
                followUp.Server.Name,
                followUp.Server.Host,
                followUp.Catalog is { } catalog ? $", database {catalog}" : string.Empty);

            knownServers.Add(followUp.Server);
            pending.Enqueue((followUp.Server, depth + 1));
        }

        return plan.FollowUps.Count;
    }

    /// <summary>
    /// Stacks the credential sources in precedence order: --db-user/--db-password, then --credentials-file,
    /// then the process environment (the registered <see cref="ICredentialProvider"/>). Each half resolves
    /// independently, so passing only a username and leaving the password in the environment works.
    /// </summary>
    private static async Task<ICredentialProvider> BuildCredentialProviderAsync(
        IServiceProvider services,
        System.CommandLine.ParseResult parseResult,
        Option<string[]> dbUserOption,
        Option<string[]> dbPasswordOption,
        Option<string?> credentialsFileOption,
        CancellationToken cancellationToken)
    {
        List<ICredentialProvider> layers = [];

        ExplicitCredentialProvider parameterCredentials = ExplicitCredentialProvider.FromArguments(
            parseResult.GetValue(dbUserOption) ?? [],
            parseResult.GetValue(dbPasswordOption) ?? []);
        if (!parameterCredentials.IsEmpty)
        {
            layers.Add(parameterCredentials);
        }

        if (parseResult.GetValue(credentialsFileOption) is { Length: > 0 } credentialsFilePath)
        {
            layers.Add(await CredentialsFileProvider.LoadAsync(Path.GetFullPath(credentialsFilePath), cancellationToken));
        }

        layers.Add(services.GetRequiredService<ICredentialProvider>());

        return layers.Count == 1 ? layers[0] : new LayeredCredentialProvider(layers);
    }
}
