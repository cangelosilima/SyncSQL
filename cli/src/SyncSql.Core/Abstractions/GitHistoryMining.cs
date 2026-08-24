using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

public sealed record ObjectHistoryInfo
{
    public required int ChangeCount { get; init; }
    public DateTimeOffset? LastChangedAt { get; init; }
    public required IReadOnlyList<CatalogObjectVersion> Versions { get; init; }
}

public sealed record GitHistoryMiningRequest
{
    public required string RepoRoot { get; init; }
    public required string PathPrefix { get; init; }
    public int HistoryLimit { get; init; } = 250;
    public int MaxVersionsPerObject { get; init; } = 15;
    public int MaxHistoryContentCalls { get; init; } = 1500;
    public int MaxCoChangeCommitSize { get; init; } = 40;

    /// <summary>Object ids known to the current catalog build - only commits/files matching one of these are attributed to a node.</summary>
    public required IReadOnlySet<string> KnownObjectIds { get; init; }
}

public sealed record GitHistoryMiningResult
{
    public required IReadOnlyList<CatalogCommit> RecentChanges { get; init; }
    public required IReadOnlyList<CoChangePair> CoChangePairs { get; init; }

    /// <summary>Object id -> its change count/last-changed-at/bounded version history.</summary>
    public required IReadOnlyDictionary<string, ObjectHistoryInfo> ObjectHistory { get; init; }

    public static GitHistoryMiningResult Empty { get; } = new()
    {
        RecentChanges = [],
        CoChangePairs = [],
        ObjectHistory = new Dictionary<string, ObjectHistoryInfo>(),
    };
}

/// <summary>
/// Mines a git checkout's own commit history for the change heatmap, co-change pairs, and per-object
/// bounded version history (with DDL fetched via `git show`) that power catalog.json's history features.
/// A direct port of Build-Catalog.ps1's "History, heatmap and point-in-time" mining pass.
/// </summary>
public interface IGitHistoryMiner
{
    public Task<GitHistoryMiningResult> MineAsync(GitHistoryMiningRequest request, CancellationToken cancellationToken);
}
