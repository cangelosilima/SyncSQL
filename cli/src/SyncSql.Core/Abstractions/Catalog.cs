using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>Inputs for building a catalog.json document - a direct port of Build-Catalog.ps1's parameters.</summary>
public sealed record CatalogBuildRequest
{
    /// <summary>Root of the extracted tree (server/database/type/[schema/]object.sql).</summary>
    public required string ObjectsRoot { get; init; }

    /// <summary>Git checkout containing -PathPrefix, mined for history/heatmap/point-in-time data. Omit to skip all of that (empty history, zero change counts) rather than failing.</summary>
    public string? RepoRoot { get; init; }

    /// <summary>Folder inside <see cref="RepoRoot"/> holding the extracted tree. Empty (the default) means the tree starts at the repository root, so the first path segment is the server name.</summary>
    public string PathPrefix { get; init; } = "";
    public int HistoryLimit { get; init; } = 250;
    public int MaxVersionsPerObject { get; init; } = 15;
    public int MaxHistoryContentCalls { get; init; } = 1500;
    public int MaxCoChangeCommitSize { get; init; } = 40;

    /// <summary>Root of the accumulating metrics history tree. Omit to skip - node.metrics is simply left empty.</summary>
    public string? MetricsRoot { get; init; }

    /// <summary>
    /// Whether lineage inference also recovers references from SQL built as a string at runtime
    /// (<c>OPENQUERY</c>, <c>EXEC</c> of a string, a variable assembled and then executed). On by default;
    /// <c>--no-dynamic-sql</c> turns it off for a build that wants only what the parse tree states
    /// outright. See <see cref="LineageAnalysisOptions.DynamicSql"/>.
    /// </summary>
    public bool DynamicSql { get; init; } = true;
}

/// <summary>
/// Orchestrates catalog assembly: reads every extracted object under ObjectsRoot, dispatches lineage
/// inference per object's engine (<see cref="ILineageAnalyzerResolver"/>), optionally mines git history
/// (<see cref="IGitHistoryMiner"/>) and attaches metrics history (<see cref="IMetricsHistoryStore"/>).
/// </summary>
public interface ICatalogBuilder
{
    public Task<Catalog> BuildAsync(CatalogBuildRequest request, CancellationToken cancellationToken);
}
