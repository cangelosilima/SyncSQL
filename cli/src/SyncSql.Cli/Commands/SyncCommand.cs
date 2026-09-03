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

            List<string> failedServers = [];
            int attemptedServers = 0;
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

                attemptedServers++;
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
                totalFiles, attemptedServers - failedServers.Count, failedServers.Count);

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
