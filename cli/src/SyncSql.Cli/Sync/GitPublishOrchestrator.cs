using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Json;

namespace SyncSql.Cli.Sync;

/// <summary>
/// The clone -&gt; fold metrics -&gt; rebuild catalog.json -&gt; commit -&gt; push sequence shared by `sync` (which
/// extracts first) and `git publish` (which publishes an already-populated staging tree, e.g. one merged
/// from several parallel per-server extraction jobs) - kept in one place so the two commands can't drift.
/// </summary>
internal static class GitPublishOrchestrator
{
    /// <summary>
    /// Publishes <paramref name="stagingRoot"/> to the configured git remote. Returns 1 (and logs) if no
    /// push token is available; otherwise runs the publish and returns 0. <paramref name="metricsRoot"/>
    /// is optional - when omitted, catalog.json is still built (from whatever metrics history the target
    /// repo's own metrics/ tree already has), it just isn't updated with new snapshots this run.
    /// </summary>
    public static async Task<int> PublishAsync(
        IServiceProvider services,
        SyncSqlConfig config,
        string stagingRoot,
        bool stagingRootExplicit,
        string? metricsRoot,
        bool metricsRootExplicit,
        string? pushToken,
        int historyLimit,
        int metricsHistoryLimit,
        string summary,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pushToken))
        {
            logger.LogError("No push token supplied. Set the CI_JOB_Maintainer_Token CI/CD variable (a Maintainer-role token with write_repository scope) or pass --push-token.");
            return 1;
        }

        string gitWorkDir = Path.Combine(Path.GetTempPath(), $"syncsql-repo-{Guid.NewGuid()}");
        IGitRepository gitRepository = services.GetRequiredService<IGitRepository>();
        IMetricsHistoryStore metricsHistoryStore = services.GetRequiredService<IMetricsHistoryStore>();
        ICatalogBuilder catalogBuilder = services.GetRequiredService<ICatalogBuilder>();

        async Task PostSyncHookAsync(string workDir, string pathPrefix, CancellationToken ct)
        {
            // Kept outside pathPrefix on purpose - PublishAsync only wipes/replaces pathPrefix, so this
            // tree accumulates across runs instead of being reset to just this run's snapshot.
            string metricsHistoryRoot = Path.Combine(workDir, "metrics");
            if (metricsRoot is not null)
            {
                logger.LogInformation("Updating metrics history ({Limit}-snapshot retention) -> {Path}", metricsHistoryLimit, metricsHistoryRoot);
                await metricsHistoryStore.UpdateAsync(new MetricsHistoryUpdateRequest
                {
                    SnapshotRoot = metricsRoot,
                    HistoryRoot = metricsHistoryRoot,
                    HistoryLimit = metricsHistoryLimit,
                }, ct);
            }
            else
            {
                logger.LogInformation("No metrics snapshot root supplied; catalog.json will be built from the target repo's existing metrics history only.");
            }

            string catalogOutputPath = Path.Combine(workDir, pathPrefix, "catalog.json");
            logger.LogInformation("Building catalog.json ({Limit}-commit history window) -> {Path}", historyLimit, catalogOutputPath);
            Core.Domain.Catalog catalog = await catalogBuilder.BuildAsync(new CatalogBuildRequest
            {
                ObjectsRoot = Path.Combine(workDir, pathPrefix),
                RepoRoot = workDir,
                PathPrefix = pathPrefix,
                HistoryLimit = historyLimit,
                MetricsRoot = metricsHistoryRoot,
            }, ct);
            await File.WriteAllTextAsync(catalogOutputPath, JsonSerializer.Serialize(catalog, SyncSqlJsonOptions.Default), ct);
        }

        try
        {
            GitPublishResult publishResult = await gitRepository.PublishAsync(new GitPublishRequest
            {
                GitConfig = config.Git.Resolved(),
                StagingRoot = stagingRoot,
                Token = pushToken,
                WorkDir = gitWorkDir,
                Summary = summary,
                CloneDepth = historyLimit,
                PostSyncHookAsync = PostSyncHookAsync,
            }, cancellationToken);

            if (publishResult.Published)
            {
                logger.LogInformation("Published extracted objects, metrics history and catalog.json.");
            }

            return 0;
        }
        finally
        {
            TryDeleteDirectory(gitWorkDir);
            if (metricsRoot is not null && !metricsRootExplicit)
            {
                TryDeleteDirectory(metricsRoot);
            }

            if (!stagingRootExplicit)
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
