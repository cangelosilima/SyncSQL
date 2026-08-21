using System.Globalization;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Catalog;

/// <summary>
/// Mines a git checkout's own commit history for the change heatmap, co-change pairs, and per-object
/// bounded version history (DDL fetched via `git show`) that power catalog.json's history features. A
/// direct port of Build-Catalog.ps1's "History, heatmap and point-in-time" mining pass. A static Pages
/// site can't run live git queries, so this all happens once, at catalog-build time, from a real
/// checkout.
/// </summary>
public sealed class GitHistoryMiner(IProcessRunner processRunner, ILogger<GitHistoryMiner> logger) : IGitHistoryMiner
{
    private const string CommitMarker = "@@COMMIT@@";

    public async Task<GitHistoryMiningResult> MineAsync(GitHistoryMiningRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(request.RepoRoot, ".git")))
        {
            logger.LogWarning("RepoRoot '{RepoRoot}' is not a git checkout (no .git) - skipping history mining.", request.RepoRoot);
            return GitHistoryMiningResult.Empty;
        }

        logger.LogInformation("Mining up to {HistoryLimit} commit(s) of git history under '{PathPrefix}' in {RepoRoot}", request.HistoryLimit, request.PathPrefix, request.RepoRoot);

        try
        {
            List<Commit> commits = await ReadCommitsAsync(request, cancellationToken);
            logger.LogInformation("Found {Count} commit(s) touching '{PathPrefix}'", commits.Count, request.PathPrefix);

            List<CatalogCommit> recentChanges = [];
            Dictionary<string, int> changeCounts = [];
            Dictionary<string, DateTimeOffset> lastChangedAt = [];
            Dictionary<string, List<CatalogObjectVersion>> objectHistory = [];
            Dictionary<string, int> coChangeCounts = [];

            string prefixWithSlash = request.PathPrefix + "/";

            foreach (Commit commit in commits)
            {
                List<string> objectIds = [.. commit.Files
                    .Where(f => f.StartsWith(prefixWithSlash, StringComparison.Ordinal) && f.EndsWith(".sql", StringComparison.Ordinal))
                    .Select(f => f[prefixWithSlash.Length..^".sql".Length])
                    .Where(request.KnownObjectIds.Contains)];

                if (objectIds.Count == 0)
                {
                    continue;
                }

                recentChanges.Add(new CatalogCommit { Sha = commit.Sha, Date = commit.Date, Message = commit.Message, ObjectIds = objectIds });

                foreach (string id in objectIds)
                {
                    changeCounts[id] = changeCounts.GetValueOrDefault(id) + 1;
                    lastChangedAt.TryAdd(id, commit.Date);

                    if (!objectHistory.TryGetValue(id, out List<CatalogObjectVersion>? versions))
                    {
                        versions = [];
                        objectHistory[id] = versions;
                    }
                    if (versions.Count < request.MaxVersionsPerObject)
                    {
                        versions.Add(new CatalogObjectVersion { Sha = commit.Sha, Date = commit.Date, Message = commit.Message, Ddl = null });
                    }
                }

                if (objectIds.Count > 1 && objectIds.Count <= request.MaxCoChangeCommitSize)
                {
                    List<string> sorted = [.. objectIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
                    for (int x = 0; x < sorted.Count; x++)
                    {
                        for (int y = x + 1; y < sorted.Count; y++)
                        {
                            string pairKey = $"{sorted[x]}|{sorted[y]}";
                            coChangeCounts[pairKey] = coChangeCounts.GetValueOrDefault(pairKey) + 1;
                        }
                    }
                }
            }

            int showCalls = await FetchHistoricalDdlAsync(request, objectHistory, cancellationToken);

            Dictionary<string, ObjectHistoryInfo> historyByObject = objectHistory.ToDictionary(
                kv => kv.Key,
                kv => new ObjectHistoryInfo
                {
                    ChangeCount = changeCounts.GetValueOrDefault(kv.Key),
                    LastChangedAt = lastChangedAt.GetValueOrDefault(kv.Key),
                    Versions = kv.Value,
                },
                StringComparer.OrdinalIgnoreCase);

            // Objects with a nonzero change count but no version-history entry (shouldn't normally
            // happen, but keeps changeCount/lastChangedAt available even if history capture was
            // skipped for some reason) still get an entry.
            foreach (string id in changeCounts.Keys)
            {
                historyByObject.TryAdd(id, new ObjectHistoryInfo
                {
                    ChangeCount = changeCounts[id],
                    LastChangedAt = lastChangedAt.GetValueOrDefault(id),
                    Versions = [],
                });
            }

            List<CoChangePair> coChangePairs = [.. coChangeCounts
                .OrderByDescending(kv => kv.Value)
                .Take(100)
                .Select(kv =>
                {
                    string[] ids = kv.Key.Split('|', 2);
                    return new CoChangePair { A = ids[0], B = ids[1], Count = kv.Value };
                })];

            logger.LogInformation(
                "History mining complete: {RecentChanges} relevant commit(s), {CoChangePairs} co-change pair(s), {ShowCalls} historical content fetch(es)",
                recentChanges.Count, coChangePairs.Count, showCalls);

            return new GitHistoryMiningResult
            {
                RecentChanges = recentChanges,
                CoChangePairs = coChangePairs,
                ObjectHistory = historyByObject,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning("History mining failed (continuing without it): {Message}", ex.Message);
            return GitHistoryMiningResult.Empty;
        }
    }

    private async Task<List<Commit>> ReadCommitsAsync(GitHistoryMiningRequest request, CancellationToken cancellationToken)
    {
        ProcessResult result = await processRunner.RunAsync(
            "git",
            [
                "-C", request.RepoRoot, "log", "-n", request.HistoryLimit.ToString(CultureInfo.InvariantCulture),
                "--date=iso-strict", $"--pretty=format:{CommitMarker}%H{CommitMarker}%ad{CommitMarker}%s", "--name-only",
                "--", request.PathPrefix,
            ],
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"git log exited {result.ExitCode}: {result.StandardError}");
        }

        List<Commit> commits = [];
        Commit? current = null;
        List<string>? currentFiles = null;

        foreach (string line in result.StandardOutput.Split('\n'))
        {
            if (line.StartsWith(CommitMarker, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    commits.Add(current with { Files = currentFiles! });
                }

                string[] parts = line[CommitMarker.Length..].Split(new[] { CommitMarker }, 3, StringSplitOptions.None);
                current = new Commit(parts[0], DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture), parts.Length > 2 ? parts[2] : string.Empty, []);
                currentFiles = [];
            }
            else if (current is not null && !string.IsNullOrWhiteSpace(line))
            {
                currentFiles!.Add(line.Trim());
            }
        }
        if (current is not null)
        {
            commits.Add(current with { Files = currentFiles! });
        }

        return commits;
    }

    private async Task<int> FetchHistoricalDdlAsync(GitHistoryMiningRequest request, Dictionary<string, List<CatalogObjectVersion>> objectHistory, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching historical DDL content (up to {MaxCalls} `git show` call(s))", request.MaxHistoryContentCalls);
        int showCalls = 0;

        foreach ((string id, List<CatalogObjectVersion> versions) in objectHistory)
        {
            if (showCalls >= request.MaxHistoryContentCalls)
            {
                break;
            }

            string path = $"{request.PathPrefix}/{id}.sql";
            for (int i = 0; i < versions.Count; i++)
            {
                if (showCalls >= request.MaxHistoryContentCalls)
                {
                    break;
                }

                showCalls++;
                ProcessResult result = await processRunner.RunAsync("git", ["-C", request.RepoRoot, "show", $"{versions[i].Sha}:{path}"], cancellationToken: cancellationToken);
                if (result.Succeeded && !string.IsNullOrEmpty(result.StandardOutput))
                {
                    ParsedObjectFile parsed = ExtractedObjectFile.Parse(result.StandardOutput.Split('\n'));
                    versions[i] = versions[i] with { Ddl = parsed.Ddl };
                }
                // Non-zero exit (e.g. renamed/deleted at that revision) leaves Ddl null - the version
                // still shows up in the timeline.
            }
        }

        return showCalls;
    }

    private sealed record Commit(string Sha, DateTimeOffset Date, string Message, List<string> Files);
}
