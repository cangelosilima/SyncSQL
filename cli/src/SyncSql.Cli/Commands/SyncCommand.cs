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
        Option<string?> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string?> outputLogOption = new("--output-log")
        {
            Description = "Append run logs to this file as well as the terminal. Accepts an absolute or relative file path; missing parent directories are created.",
        };
        Option<string?> stagingRootOption = new("--staging-root")
        {
            Description = "Directory each extracted object is written to, as <server>/<database>/<schema>/<type>/<object>.sql. Default: uppercase servers.type (MSSQL/ORACLE), or --output-root when supplied.",
        };
        Option<string?> metricsSnapshotRootOption = new("--metrics-snapshot-root")
        {
            Description = $"Directory this run's volatile metrics snapshots are written to (separate from --staging-root - one JSON file per table, folded into history later via `syncsql metrics update`). Default: <output-root>/{SyncSqlPaths.MetricsSnapshotDirectoryName}.",
        };
        Option<bool> skipMetricsOption = new("--skip-metrics")
        {
            Description = "Skip capturing volatile table metrics during extraction.",
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
        Option<int> maxParallelismOption = new("--max-parallelism")
        {
            Description = "Maximum concurrent extraction jobs across all engines. Slots are shared fairly and reassigned as work finishes. Use 1 for sequential extraction.",
            DefaultValueFactory = _ => 4,
        };
        maxParallelismOption.Validators.Add(result =>
        {
            if (result.Tokens.Count == 1
                && int.TryParse(result.Tokens[0].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value)
                && value < 1)
            {
                result.AddError("--max-parallelism must be at least 1.");
            }
        });

        Command command = new("sync", "Extract every configured server's database objects and metrics snapshots.")
        {
            configOption,
            outputRootOption,
            outputLogOption,
            stagingRootOption,
            metricsSnapshotRootOption,
            skipMetricsOption,
            dbUserOption,
            dbPasswordOption,
            credentialsFileOption,
            serverIncludeOption,
            serverExcludeOption,
            maxParallelismOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(SyncCommand));

            SyncSqlFileLoggerProvider? fileLog = null;
            try
            {
                if (parseResult.GetValue(outputLogOption) is { } logPath)
                {
                    fileLog = new(logPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                logger.LogError("Cannot open output log: {Message}", ex.Message);
                return 1;
            }
            using SyncSqlFileLoggerProvider? logScope = fileLog;
            if (fileLog is not null)
            {
                services.GetRequiredService<ILoggerFactory>().AddProvider(fileLog);
            }

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
            List<string> partialServers = [];
            List<string> partialDiscoveredServers = [];
            // A server nobody configured is a lead this run chose to chase; failing to reach one says
            // something about the fleet, not about this run's job, so it's reported without failing it.
            List<string> failedDiscoveredServers = [];
            int attemptedServers = 0;
            int totalFiles = 0;
            int discoveredServers = 0;
            bool captureMetrics = !parseResult.GetValue(skipMetricsOption);

            // Configured servers first; anything reached by following their linked servers is appended
            // as its own round, so a link found at depth N is extracted at depth N+1 and can in turn be
            // followed (up to discovery.linkedServers.maxDepth).
            List<ServerConfig> knownServers = [.. config.Servers];
            List<ServerConfig> coveredServers = [.. config.Servers.Where(server => serverSelection.IsAllowed(server.Name)
                && EffectiveFilters.Resolve(config.Defaults, server).ObjectTypes.Count > 0)];
            Queue<(ServerConfig Server, int Depth)> pending = new(config.Servers.Select(server => (server, 0)));
            ExtractionWorkScheduler scheduler = new(parseResult.GetValue(maxParallelismOption));
            SyncSqlTerminal terminal = services.GetService<SyncSqlTerminal>() ?? new(TextWriter.Null, animated: false);
            await using ExtractionProgressDisplay progress = terminal.StartExtraction();
            foreach (ServerConfig server in config.Servers)
            {
                progress.Add(server.Name);
            }

            while (pending.Count > 0)
            {
                List<(ServerConfig Server, int Depth, EffectiveFilters Filters, DatabaseCredentials Credentials)> work = [];
                while (pending.TryDequeue(out var entry))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (ServerConfig server, int depth) = entry;

                    if (depth == 0 && !serverSelection.IsAllowed(server.Name))
                    {
                        progress.Complete(server.Name, "Skipped", "Excluded by server filter");
                        logger.LogInformation("Skipping '{Server}' (excluded by server selection filter)", server.Name);
                        continue;
                    }

                    EffectiveFilters filters = EffectiveFilters.Resolve(config.Defaults, server);
                    if (filters.ObjectTypes.Count == 0)
                    {
                        progress.Complete(server.Name, "Skipped", "No object types configured");
                        logger.LogWarning("Skipping '{Server}': no objectTypes configured (defaults + server override both empty).", server.Name);
                        continue;
                    }

                    attemptedServers++;
                    try
                    {
                        DatabaseCredentials credentials = ResolveCredentials(server, credentialProvider, OperatingSystem.IsWindows());
                        work.Add((server, depth, filters, credentials));
                    }
                    catch (InvalidOperationException ex)
                    {
                        coveredServers.Remove(server);
                        progress.Complete(server.Name, "Failed", "Missing credentials");
                        logger.LogError("Skipping '{Server}': {Message}", server.Name, ex.Message);
                        (depth == 0 ? failedServers : failedDiscoveredServers).Add(server.Name);
                        continue;
                    }
                }

                // Each worker owns its connections and output. Fold the small summaries in input
                // order after the round so competing links always choose the same parent/credentials.
                ServerExtractionResult[] results = new ServerExtractionResult[work.Count];
                ServerIdentityRegistry identities = new(knownServers.Select(ServerIdentity.FromConfig));
                async Task<int> ExtractServerAsync(int index, ExtractionWorkContext context)
                {
                    CancellationToken workerToken = context.CancellationToken;
                    workerToken.ThrowIfCancellationRequested();
                    var (server, depth, filters, credentials) = work[index];
                    bool discoverHere = followLinkedServers && depth < discovery.MaxDepth && server.Type == DatabaseEngine.MsSql;
                    IProgress<ExtractionProgress> serverProgress = progress.Start(server.Name);

                    try
                    {
                        string outputRoot = parseResult.GetValue(outputRootOption) ?? SyncSqlPaths.DefaultOutputRoot(server.Type);
                        string stagingRoot = SyncSqlPaths.Resolve(parseResult.GetValue(stagingRootOption), outputRoot, SyncSqlPaths.ObjectsRelativePath);
                        string metricsRoot = SyncSqlPaths.Resolve(parseResult.GetValue(metricsSnapshotRootOption), outputRoot, SyncSqlPaths.MetricsSnapshotDirectoryName);
                        Directory.CreateDirectory(stagingRoot);
                        if (captureMetrics)
                        {
                            Directory.CreateDirectory(metricsRoot);
                        }
                        if (captureMetrics)
                        {
                            logger.LogInformation("[{Server}] Writing objects to {StagingRoot}; snapshots to {MetricsRoot}", server.Name, stagingRoot, metricsRoot);
                        }
                        else
                        {
                            logger.LogInformation("[{Server}] Writing objects to {StagingRoot}; metrics capture skipped", server.Name, stagingRoot);
                        }
                        IDatabaseObjectExtractor extractor = extractorResolver.Resolve(server.Type);
                        workerToken.ThrowIfCancellationRequested();
                        ExtractionOutcome outcome = await extractor.ExtractAsync(
                            server, filters, new ExtractionOptions
                            {
                                Credentials = credentials,
                                CaptureMetrics = captureMetrics,
                                MaxParallelism = scheduler.MaxParallelism,
                                WorkContext = context,
                                DiscoverLinkedServers = discoverHere,
                                Progress = serverProgress,
                            }, workerToken);

                        await ExtractionOutputWriter.WriteAsync(outcome, stagingRoot, metricsRoot, workerToken, server.ExportPath, serverProgress, identities.Describe(ServerIdentity.FromConfig(server)), logger);

                        results[index] = new(outcome.Objects.Count, discoverHere ? outcome.DiscoveredLinkedServers : [], Failed: false, Partial: outcome.IsPartial);
                        progress.Complete(server.Name, outcome.IsPartial ? "Partially complete" : "Done",
                            outcome.IsPartial ? $"Failed databases: {string.Join(", ", outcome.FailedDatabases.Select(failure => failure.Database))}"
                                : captureMetrics ? "Objects and snapshots written" : "Objects written", outcome.Objects.Count);
                        foreach (DatabaseExtractionFailure failure in outcome.FailedDatabases)
                        {
                            logger.LogWarning("[{Server}/{Database}] Partially complete: {Message}", server.Name, failure.Database, failure.Message);
                        }
                        logger.LogInformation("- {Server} ({Engine}): {Count} object file(s)", server.Name, server.Type.ToConfigString(), outcome.Objects.Count);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        progress.Complete(server.Name, "Failed", "See error above");
                        if (depth == 0)
                        {
                            logger.LogError("Extraction failed for '{Server}': {Message}", server.Name, ex.Message);
                        }
                        else
                        {
                            logger.LogWarning("Extraction failed for discovered server '{Server}': {Message}", server.Name, ex.Message);
                        }
                        results[index] = new(0, [], Failed: true);
                    }
                    return index;
                }

                await scheduler.RunAsync(work.Select((entry, index) =>
                    (entry.Server.Type, (Func<ExtractionWorkContext, Task<int>>)(context => ExtractServerAsync(index, context)))), cancellationToken);
                // Failed attempts establish no coverage, even when another worker in this round
                // discovers a link back to them. Keep their address registrations for resolution.
                for (int index = 0; index < work.Count; index++)
                {
                    if (results[index].Failed)
                    {
                        coveredServers.Remove(work[index].Server);
                    }
                }
                for (int index = 0; index < work.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (server, depth, _, credentials) = work[index];
                    ServerExtractionResult result = results[index];
                    if (result.Failed)
                    {
                        (depth == 0 ? failedServers : failedDiscoveredServers).Add(server.Name);
                        continue;
                    }

                    totalFiles += result.FileCount;
                    if (result.Partial)
                    {
                        (depth == 0 ? partialServers : partialDiscoveredServers).Add(server.Name);
                    }
                    if (result.LinkedServers.Count > 0)
                    {
                        discoveredServers += QueueLinkedServers(
                            logger, server, credentials.Username, result.LinkedServers, discovery, depth, knownServers, pending, config.Defaults, coveredServers);
                    }
                }
                foreach (var entry in pending)
                {
                    progress.Add(entry.Server.Name);
                }
            }

            await progress.DisposeAsync();
            logger.LogInformation(
                "Extraction {Completion}: {TotalFiles} object file(s) across {ServerCount} server(s) ({DiscoveredCount} reached through a linked server); {PartialCount} partially complete server(s); {FailureCount} failure(s).",
                partialServers.Count + partialDiscoveredServers.Count > 0 ? "partially complete" : "complete",
                totalFiles, attemptedServers - failedServers.Count - failedDiscoveredServers.Count, discoveredServers,
                partialServers.Count + partialDiscoveredServers.Count, failedServers.Count);

            if (partialServers.Count + partialDiscoveredServers.Count > 0)
            {
                logger.LogWarning("Partially complete server(s): {Servers}", string.Join(", ", partialServers.Concat(partialDiscoveredServers)));
            }

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

            return partialServers.Count > 0 ? 1 : 0;
        });

        return command;
    }

    private sealed record ServerExtractionResult(int FileCount, IReadOnlyList<DiscoveredLinkedServer> LinkedServers, bool Failed, bool Partial = false);

    internal static DatabaseCredentials ResolveCredentials(ServerConfig server, ICredentialProvider provider, bool isWindows)
    {
        if (!server.IntegratedSecurity)
        {
            return provider.Resolve(server.CredentialsVariablePrefix);
        }

        // Environment resolves the current OS account. Keep Windows domain qualification
        // for linked-server login matching; SqlClient itself uses the OS identity token.
        string username = isWindows
            ? $"{Environment.UserDomainName}\\{Environment.UserName}"
            : Environment.UserName;
        return new DatabaseCredentials(username, "");
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
        Queue<(ServerConfig Server, int Depth)> pending,
        ObjectFilterSet? defaults,
        List<ServerConfig> coveredServers)
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(parent, parentUsername, discovered, discovery, knownServers, defaults, coveredServers);

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
            coveredServers.Add(followUp.Server);
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
            parseResult.GetValue(dbUserOption)!,
            parseResult.GetValue(dbPasswordOption)!);
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
