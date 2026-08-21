using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>Inputs for building a catalog.json document - a direct port of Build-Catalog.ps1's parameters.</summary>
public sealed record CatalogBuildRequest
{
    /// <summary>Root of the extracted tree (server/database/type/[schema/]object.sql).</summary>
    public required string ObjectsRoot { get; init; }

    /// <summary>Git checkout containing -PathPrefix, mined for history/heatmap/point-in-time data. Omit to skip all of that (empty history, zero change counts) rather than failing.</summary>
    public string? RepoRoot { get; init; }

    public string PathPrefix { get; init; } = "objects";
    public int HistoryLimit { get; init; } = 250;
    public int MaxVersionsPerObject { get; init; } = 15;
    public int MaxHistoryContentCalls { get; init; } = 1500;
    public int MaxCoChangeCommitSize { get; init; } = 40;

    /// <summary>Root of the accumulating metrics history tree. Omit to skip - node.metrics is simply left empty.</summary>
    public string? MetricsRoot { get; init; }
}

/// <summary>
/// Orchestrates catalog assembly: reads every extracted object under ObjectsRoot, dispatches lineage
/// inference per object's engine (<see cref="ILineageAnalyzerResolver"/>), optionally mines git history
/// (<see cref="IGitHistoryMiner"/>) and attaches metrics history (<see cref="IMetricsHistoryStore"/>).
/// </summary>
public interface ICatalogBuilder
{
    Task<Catalog> BuildAsync(CatalogBuildRequest request, CancellationToken cancellationToken);
}
