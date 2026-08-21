using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

public sealed record MetricsHistoryUpdateRequest
{
    /// <summary>Root of this run's freshly captured snapshot tree (one JSON file per object, same relative path/id as the object's own .sql file).</summary>
    public required string SnapshotRoot { get; init; }

    /// <summary>Root of the accumulating history tree (kept outside config.git.pathPrefix so it survives the object tree's wipe-and-replace).</summary>
    public required string HistoryRoot { get; init; }

    /// <summary>Maximum snapshots retained per object; oldest are trimmed first.</summary>
    public int HistoryLimit { get; init; } = 90;
}

/// <summary>
/// Folds each run's freshly captured metrics snapshots into a growing per-object history array, kept
/// entirely separate from the object's own versioned DDL file. A direct port of
/// Update-MetricsHistory.ps1.
/// </summary>
public interface IMetricsHistoryStore
{
    /// <summary>Appends every snapshot under request.SnapshotRoot into its object's history file under request.HistoryRoot. Returns the number of objects updated.</summary>
    Task<int> UpdateAsync(MetricsHistoryUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Loads one object's accumulated metrics history (oldest first), or an empty list if it has none.</summary>
    Task<IReadOnlyList<MetricsSnapshot>> LoadHistoryAsync(string historyRoot, string objectId, CancellationToken cancellationToken);
}
