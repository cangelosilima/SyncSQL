using System.Text;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Catalog;

/// <summary>
/// Walks a tree produced by extraction and builds the catalog.json document site/ consumes: every
/// object becomes a node, lineage edges are inferred per node's engine (dispatched through
/// <see cref="ILineageAnalyzerResolver"/> - real T-SQL/PL-SQL parsing, not text matching), column-level
/// tags are attached using each analyzer's own alias bindings, git history is optionally mined, and
/// metrics history is optionally attached. A direct port of Build-Catalog.ps1.
/// </summary>
public sealed class CatalogBuilder(
    ILineageAnalyzerResolver lineageAnalyzerResolver,
    IGitHistoryMiner gitHistoryMiner,
    IMetricsHistoryStore metricsHistoryStore,
    IClock clock,
    ILogger<CatalogBuilder> logger) : ICatalogBuilder
{
    public async Task<Core.Domain.Catalog> BuildAsync(CatalogBuildRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.ObjectsRoot))
        {
            throw new DirectoryNotFoundException($"Objects root not found: {request.ObjectsRoot}");
        }

        logger.LogInformation("Scanning {ObjectsRoot}", request.ObjectsRoot);
        string[] files = Directory.GetFiles(request.ObjectsRoot, "*.sql", SearchOption.AllDirectories);
        logger.LogInformation("Found {Count} object file(s)", files.Length);

        List<CatalogNode> nodes = [];
        foreach (string file in files)
        {
            CatalogNode? node = await TryLoadNodeAsync(request.ObjectsRoot, file, cancellationToken);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        NodeIndex nodeIndex = new(nodes);
        Dictionary<string, CatalogNode> nodesById = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Inferring lineage edges");
        (List<CatalogEdge> edges, Dictionary<string, LineageAnalysisResult> analysisByNodeId, List<CatalogOrphanedReference> orphanedReferences) = InferLineage(nodes, nodeIndex);
        if (orphanedReferences.Count > 0)
        {
            logger.LogWarning("Found {Count} orphaned reference(s) - an object's DDL refers to something that no longer exists in scope", orphanedReferences.Count);
        }

        logger.LogInformation("Detecting column-level references for inferred edges");
        TagColumnReferences(edges, nodesById, analysisByNodeId, nodeIndex);

        Dictionary<string, int> typeCounts = [];
        foreach (CatalogNode node in nodes)
        {
            typeCounts[node.Type] = typeCounts.GetValueOrDefault(node.Type) + 1;
        }

        if (request.MetricsRoot is not null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                IReadOnlyList<MetricsSnapshot> metrics = await metricsHistoryStore.LoadHistoryAsync(request.MetricsRoot, nodes[i].Id, cancellationToken);
                if (metrics.Count > 0)
                {
                    nodes[i] = nodes[i] with { Metrics = metrics };
                }
            }
        }

        List<CatalogCommit> recentChanges = [];
        List<CoChangePair> coChangePairs = [];
        if (request.RepoRoot is not null)
        {
            GitHistoryMiningResult history = await gitHistoryMiner.MineAsync(new GitHistoryMiningRequest
            {
                RepoRoot = request.RepoRoot,
                PathPrefix = request.PathPrefix,
                HistoryLimit = request.HistoryLimit,
                MaxVersionsPerObject = request.MaxVersionsPerObject,
                MaxHistoryContentCalls = request.MaxHistoryContentCalls,
                MaxCoChangeCommitSize = request.MaxCoChangeCommitSize,
                KnownObjectIds = nodesById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            }, cancellationToken);

            recentChanges = [.. history.RecentChanges];
            coChangePairs = [.. history.CoChangePairs];

            for (int i = 0; i < nodes.Count; i++)
            {
                if (history.ObjectHistory.TryGetValue(nodes[i].Id, out ObjectHistoryInfo? info))
                {
                    nodes[i] = nodes[i] with { ChangeCount = info.ChangeCount, LastChangedAt = info.LastChangedAt, History = info.Versions };
                }
            }
        }

        logger.LogInformation("Built {NodeCount} node(s), {EdgeCount} edge(s)", nodes.Count, edges.Count);

        return new Core.Domain.Catalog
        {
            GeneratedAt = clock.UtcNow,
            Servers = [.. nodes.Select(n => n.Server).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
            TypeCounts = typeCounts,
            Nodes = nodes,
            Edges = edges,
            RecentChanges = recentChanges,
            CoChangePairs = coChangePairs,
            OrphanedReferences = orphanedReferences,
        };
    }

    private async Task<CatalogNode?> TryLoadNodeAsync(string objectsRoot, string filePath, CancellationToken cancellationToken)
    {
        string relative = Path.GetRelativePath(objectsRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
        string[] segments = relative.Split('/');
        if (segments.Length is < 4 or > 5)
        {
            logger.LogWarning("Skipping unexpected path shape: {Relative}", relative);
            return null;
        }

        string server = segments[0];
        string database = segments[1];
        string type = segments[2];
        string name = Path.GetFileNameWithoutExtension(segments[^1]);
        string? schema = segments.Length == 5 ? segments[3] : null;

        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        ParsedObjectFile parsed = ExtractedObjectFile.Parse(lines);
        string id = relative[..^".sql".Length];
        string qualifiedName = string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";

        return new CatalogNode
        {
            Id = id,
            Server = server,
            Database = database,
            Schema = schema,
            Type = type,
            Name = name,
            QualifiedName = qualifiedName,
            Path = relative,
            Ddl = parsed.Ddl,
            Description = parsed.Description,
            Columns = parsed.Columns,
            Grants = parsed.Grants,
            Sections = parsed.Sections,
            Engine = parsed.Engine,
            SizeBytes = Encoding.UTF8.GetByteCount(parsed.Ddl),
        };
    }

    private (List<CatalogEdge> Edges, Dictionary<string, LineageAnalysisResult> AnalysisByNodeId, List<CatalogOrphanedReference> OrphanedReferences) InferLineage(List<CatalogNode> nodes, NodeIndex nodeIndex)
    {
        HashSet<string> edgeKeys = [];
        List<CatalogEdge> edges = [];
        Dictionary<string, LineageAnalysisResult> analysisByNodeId = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> orphanKeys = [];
        List<CatalogOrphanedReference> orphanedReferences = [];

        foreach (CatalogNode node in nodes)
        {
            if (node.Engine is not { } engine)
            {
                // No "-- Engine:" header (a file predating that field, or a foreign file this tool
                // didn't produce) - lineage inference is simply skipped for it rather than guessed at.
                continue;
            }

            ExtractedSection? foreignKeys = node.Sections.FirstOrDefault(s => s.Title == "Foreign Keys");
            string scanText = foreignKeys is not null ? $"{node.Ddl}\n{foreignKeys.Content}" : node.Ddl;
            if (string.IsNullOrWhiteSpace(scanText))
            {
                continue;
            }

            ILineageAnalyzer analyzer = lineageAnalyzerResolver.Resolve(engine);
            LineageAnalysisResult analysis = analyzer.Analyze(scanText);
            analysisByNodeId[node.Id] = analysis;

            foreach (ObjectRef reference in analysis.ObjectRefs)
            {
                ReferenceResolution resolution = nodeIndex.Resolve(node, reference);
                if (resolution.Kind == ReferenceResolutionKind.NotFound)
                {
                    if (orphanKeys.Add($"{node.Id}|{reference.Schema}|{reference.Name}"))
                    {
                        orphanedReferences.Add(new CatalogOrphanedReference { From = node.Id, Schema = reference.Schema, Name = reference.Name });
                    }
                    continue;
                }

                if (resolution is not { Kind: ReferenceResolutionKind.Resolved, NodeId: { } targetId } || targetId == node.Id)
                {
                    continue;
                }

                if (edgeKeys.Add($"{node.Id}|{targetId}"))
                {
                    edges.Add(new CatalogEdge { From = node.Id, To = targetId });
                }
            }
        }

        orphanedReferences.Sort((a, b) =>
        {
            int byFrom = string.Compare(a.From, b.From, StringComparison.OrdinalIgnoreCase);
            return byFrom != 0 ? byFrom : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return (edges, analysisByNodeId, orphanedReferences);
    }

    private static void TagColumnReferences(
        List<CatalogEdge> edges,
        IReadOnlyDictionary<string, CatalogNode> nodesById,
        IReadOnlyDictionary<string, LineageAnalysisResult> analysisByNodeId,
        NodeIndex nodeIndex)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            CatalogEdge edge = edges[i];
            if (!nodesById.TryGetValue(edge.To, out CatalogNode? toNode) || !nodesById.TryGetValue(edge.From, out CatalogNode? fromNode))
            {
                continue;
            }
            if (toNode.Columns.Count == 0 || !analysisByNodeId.TryGetValue(fromNode.Id, out LineageAnalysisResult? analysis))
            {
                continue;
            }

            HashSet<string> targetColumnNames = new(toNode.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            HashSet<string> matchingAliases = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string alias, ObjectRef aliasTarget) in analysis.Aliases)
            {
                if (nodeIndex.Resolve(fromNode, aliasTarget) is { Kind: ReferenceResolutionKind.Resolved, NodeId: { } aliasTargetId } && aliasTargetId == toNode.Id)
                {
                    matchingAliases.Add(alias);
                }
            }

            if (matchingAliases.Count == 0)
            {
                continue;
            }

            HashSet<string> usedColumns = new(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnRef columnRef in analysis.ColumnRefs)
            {
                if (matchingAliases.Contains(columnRef.AliasOrTable) && targetColumnNames.Contains(columnRef.Column))
                {
                    usedColumns.Add(columnRef.Column);
                }
            }

            if (usedColumns.Count > 0)
            {
                edges[i] = edge with { Columns = [.. usedColumns.Order(StringComparer.OrdinalIgnoreCase)] };
            }
        }
    }
}
