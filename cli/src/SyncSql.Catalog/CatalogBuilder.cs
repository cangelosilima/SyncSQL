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

        LinkedServerMap linkedServers = LinkedServerMap.FromNodes(nodes);
        NodeIndex nodeIndex = new(nodes, linkedServers);
        Dictionary<string, CatalogNode> nodesById = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Inferring lineage edges");
        LineageInferenceResult lineage = InferLineage(nodes, nodeIndex);
        List<CatalogEdge> edges = lineage.Edges;
        Dictionary<string, LineageAnalysisResult> analysisByNodeId = lineage.AnalysisByNodeId;
        List<CatalogOrphanedReference> orphanedReferences = lineage.OrphanedReferences;
        if (orphanedReferences.Count > 0)
        {
            logger.LogWarning("Found {Count} orphaned reference(s) - an object's DDL refers to something that no longer exists in scope", orphanedReferences.Count);
        }
        if (lineage.LinkedServerReferences.Count > 0)
        {
            logger.LogInformation(
                "Followed {Count} reference(s) across a linked server / database link, {Unresolved} of them to objects outside the catalog's scope",
                lineage.LinkedServerReferences.Count,
                lineage.LinkedServerReferences.Count(r => r.To is null));
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
            LinkedServerReferences = lineage.LinkedServerReferences,
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

    /// <summary>What one pass of lineage inference produced, beyond the edges themselves.</summary>
    private sealed record LineageInferenceResult
    {
        public required List<CatalogEdge> Edges { get; init; }

        public required Dictionary<string, LineageAnalysisResult> AnalysisByNodeId { get; init; }

        public required List<CatalogOrphanedReference> OrphanedReferences { get; init; }

        public required List<CatalogLinkedServerReference> LinkedServerReferences { get; init; }
    }

    private LineageInferenceResult InferLineage(List<CatalogNode> nodes, NodeIndex nodeIndex)
    {
        HashSet<string> edgeKeys = [];
        List<CatalogEdge> edges = [];
        Dictionary<string, LineageAnalysisResult> analysisByNodeId = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> orphanKeys = [];
        List<CatalogOrphanedReference> orphanedReferences = [];
        HashSet<string> linkedReferenceKeys = [];
        List<CatalogLinkedServerReference> linkedServerReferences = [];

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

                // A reference that crossed a linked server / database link is recorded against that
                // link whether or not the target itself is extracted, so the link object can list
                // everything reached through it - including the parts of the fleet nobody extracts.
                if (resolution.ViaLink is { } link)
                {
                    if (linkedReferenceKeys.Add($"{node.Id}|{link.NodeId}|{reference.Database}|{reference.Schema}|{reference.Name}"))
                    {
                        linkedServerReferences.Add(new CatalogLinkedServerReference
                        {
                            LinkedServer = link.NodeId,
                            From = node.Id,
                            To = resolution.NodeId,
                            Database = reference.Database ?? link.DefaultDatabase,
                            Schema = reference.Schema,
                            Name = reference.Name,
                        });
                    }

                    // The link itself becomes a hop in the graph: the caller depends on the link, and
                    // the link on whatever it reaches. Drawing it that way (rather than one direct edge
                    // to the remote object) is what makes "which objects go through this linked server"
                    // answerable from the lineage graph, and keeps a hop out of the catalog's scope
                    // visible instead of silently absent.
                    AddEdge(node.Id, link.NodeId);
                    if (resolution.NodeId is { } linkedTargetId)
                    {
                        AddEdge(link.NodeId, linkedTargetId);
                    }

                    // The link landed on a server and database the catalog does have, and the object
                    // still isn't there - that's dangling in exactly the same way a local miss is, so it
                    // belongs in the orphan list too (an out-of-scope hop resolves as External and
                    // deliberately doesn't).
                    if (resolution.Kind != ReferenceResolutionKind.NotFound)
                    {
                        continue;
                    }
                }

                if (resolution.Kind == ReferenceResolutionKind.NotFound)
                {
                    if (orphanKeys.Add($"{node.Id}|{reference.Server}|{reference.Database}|{reference.Schema}|{reference.Name}"))
                    {
                        orphanedReferences.Add(new CatalogOrphanedReference
                        {
                            From = node.Id,
                            Server = reference.Server,
                            Database = reference.Database,
                            Schema = reference.Schema,
                            Name = reference.Name,
                        });
                    }
                    continue;
                }

                if (resolution is not { Kind: ReferenceResolutionKind.Resolved, NodeId: { } targetId } || targetId == node.Id)
                {
                    continue;
                }

                AddEdge(node.Id, targetId);
            }
        }

        orphanedReferences.Sort((a, b) =>
        {
            int byFrom = string.Compare(a.From, b.From, StringComparison.OrdinalIgnoreCase);
            return byFrom != 0 ? byFrom : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        linkedServerReferences.Sort((a, b) =>
        {
            int byLink = string.Compare(a.LinkedServer, b.LinkedServer, StringComparison.OrdinalIgnoreCase);
            if (byLink != 0)
            {
                return byLink;
            }
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.From, b.From, StringComparison.OrdinalIgnoreCase);
        });

        return new LineageInferenceResult
        {
            Edges = edges,
            AnalysisByNodeId = analysisByNodeId,
            OrphanedReferences = orphanedReferences,
            LinkedServerReferences = linkedServerReferences,
        };

        void AddEdge(string from, string to)
        {
            if (from != to && edgeKeys.Add($"{from}|{to}"))
            {
                edges.Add(new CatalogEdge { From = from, To = to });
            }
        }
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
