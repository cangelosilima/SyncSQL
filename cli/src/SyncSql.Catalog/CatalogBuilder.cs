using System.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
    TimeProvider timeProvider,
    ILogger<CatalogBuilder> logger) : ICatalogBuilder
{
    public async Task<Core.Domain.Catalog> BuildAsync(CatalogBuildRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.ObjectsRoot))
        {
            throw new DirectoryNotFoundException($"Objects root not found: {request.ObjectsRoot}");
        }

        List<CatalogNode> nodes = [];
        Dictionary<string, string?> metricsRootByPath = new(FileSystemPaths.Comparer);
        IReadOnlyList<CatalogInput> inputs = request.Inputs.Count > 0 ? request.Inputs
            : [new CatalogInput { ObjectsRoot = request.ObjectsRoot, MetricsRoot = request.MetricsRoot }];
        HashSet<string> seenFiles = new(FileSystemPaths.Comparer);
        request.Progress?.Report(new("Scanning files", Unit: "files"));
        foreach (CatalogInput input in inputs)
        {
            if (!Directory.Exists(input.ObjectsRoot))
            {
                throw new DirectoryNotFoundException($"Objects root not found: {input.ObjectsRoot}");
            }
            logger.LogInformation("Scanning {ObjectsRoot}", input.ObjectsRoot);
            foreach (string file in Directory.EnumerateFiles(input.ObjectsRoot, "*.sql", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenFiles.Add(Path.GetFullPath(file)))
                {
                    continue;
                }
                request.Progress?.Report(new("Scanning files", seenFiles.Count - 1, Current: file, Unit: "files", Nodes: nodes.Count));
                CatalogNode? node = await TryLoadNodeAsync(input.ObjectsRoot, file, cancellationToken);
                if (node is not null)
                {
                    node = node with { SourcePath = node.Path, Path = Path.GetRelativePath(request.ObjectsRoot, file).Replace(Path.DirectorySeparatorChar, '/') };
                    nodes.Add(node);
                    metricsRootByPath[node.Path] = request.MetricsRoot ?? input.MetricsRoot;
                }
            }
        }

        request.Progress?.Report(new("Scanning files", seenFiles.Count, seenFiles.Count, Unit: "files", Nodes: nodes.Count));
        request.Progress?.Report(new("Resolving identities", Nodes: nodes.Count));
        foreach (var server in nodes.GroupBy(node => node.Server, StringComparer.OrdinalIgnoreCase))
        {
            if (server.Select(node => node.Engine).OfType<DatabaseEngine>().Distinct().Count() > 1)
            {
                throw new InvalidDataException($"Server name '{server.Key}' is used by multiple engines. Configure distinct server names before consolidating.");
            }
        }

        Dictionary<string, string> sourceIdsByPath = nodes.ToDictionary(n => n.Path, n => n.Id, FileSystemPaths.Comparer);
        nodes = CatalogServerIdentity.Canonicalize(nodes);
        var metricSourcesByObject = nodes.GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(n => (Root: metricsRootByPath[n.Path], Id: sourceIdsByPath[n.Path])).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> objectPaths = nodes.ToDictionary(n => n.Path, n => n.Id, FileSystemPaths.Comparer);
        LinkedServerMap linkedServers = LinkedServerMap.FromNodes(nodes);
        nodes = CatalogServerIdentity.MergeObjects(nodes);
        nodes = [.. nodes.Select(node => linkedServers.MetadataFor(node.Id) is { } metadata ? node with { Link = metadata } : node)];
        NodeIndex nodeIndex = new(nodes, linkedServers);
        Dictionary<string, CatalogNode> nodesById = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Inferring lineage edges and column references");
        LineageInferenceResult lineage = InferLineage(nodes, nodesById, nodeIndex, new LineageAnalysisOptions { DynamicSql = request.DynamicSql }, request.Progress, cancellationToken);
        List<CatalogEdge> edges = lineage.Edges;
        List<CatalogOrphanedReference> orphanedReferences = lineage.OrphanedReferences;
        if (orphanedReferences.Count > 0)
        {
            logger.LogWarning("Found {Count} orphaned reference(s) - an object's DDL refers to something that no longer exists in scope", orphanedReferences.Count);
        }
        if (lineage.SystemReferences.Count > 0)
        {
            logger.LogInformation("Recognized {Count} reference(s) to engine-provided objects (not orphans)", lineage.SystemReferences.Count);
        }
        if (lineage.LinkedServerReferences.Count > 0)
        {
            logger.LogInformation(
                "Followed {Count} reference(s) across a linked server / database link, {Unresolved} of them to objects outside the catalog's scope",
                lineage.LinkedServerReferences.Count,
                lineage.LinkedServerReferences.Count(r => r.To is null));
        }

        Dictionary<string, int> typeCounts = [];
        foreach (CatalogNode node in nodes)
        {
            typeCounts[node.Type] = typeCounts.GetValueOrDefault(node.Type) + 1;
        }

        if (metricsRootByPath.Values.Any(root => root is not null))
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.Progress?.Report(new("Loading metrics", i, nodes.Count, nodes[i].Id));
                List<MetricsSnapshot> metrics = [];
                foreach (var source in metricSourcesByObject[nodes[i].Id])
                {
                    if (source.Root is not null)
                    {
                        metrics.AddRange(await metricsHistoryStore.LoadHistoryAsync(source.Root, source.Id, cancellationToken));
                    }
                }
                if (metrics.Count > 0)
                {
                    // Compare the complete payload, including nested collections. Different samples
                    // captured at the same time must survive; identical alias copies must not.
                    nodes[i] = nodes[i] with
                    {
                        Metrics = [.. metrics.DistinctBy(snapshot => JsonSerializer.Serialize(snapshot))
                            .OrderBy(snapshot => snapshot.CapturedAt)],
                    };
                }
            }
            request.Progress?.Report(new("Loading metrics", nodes.Count, nodes.Count));
        }

        List<CatalogCommit> recentChanges = [];
        List<CoChangePair> coChangePairs = [];
        if (request.RepoRoot is not null)
        {
            request.Progress?.Report(new("Mining Git history", Current: request.RepoRoot, Unit: "commits"));
            GitHistoryMiningResult history = await gitHistoryMiner.MineAsync(new GitHistoryMiningRequest
            {
                RepoRoot = request.RepoRoot,
                PathPrefix = request.PathPrefix,
                HistoryLimit = request.HistoryLimit,
                MaxVersionsPerObject = request.MaxVersionsPerObject,
                MaxHistoryContentCalls = request.MaxHistoryContentCalls,
                MaxCoChangeCommitSize = request.MaxCoChangeCommitSize,
                KnownObjectIds = nodesById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                ObjectPaths = objectPaths,
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
        request.Progress?.Report(new("Assembling catalog", nodes.Count, nodes.Count, Nodes: nodes.Count, Edges: edges.Count));

        List<CatalogServer> serverDetails = [.. nodes.GroupBy(n => n.Server, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                CatalogNode representative = group.FirstOrDefault(n => n.ServerIdentity is not null) ?? group.First();
                SyncSql.Core.Configuration.ServerIdentity? identity = representative.ServerIdentity;
                return new CatalogServer
                {
                    Name = group.Key,
                    Hostname = identity?.Host,
                    Environment = identity?.Environment,
                    Tags = identity?.Tags ?? [],
                    Engine = representative.Engine,
                };
            })
            .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)];

        return new Core.Domain.Catalog
        {
            GeneratedAt = timeProvider.GetUtcNow(),
            Servers = [.. nodes.Select(n => n.Server).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
            ServerDetails = serverDetails,
            TypeCounts = typeCounts,
            Nodes = nodes,
            Edges = edges,
            RecentChanges = recentChanges,
            CoChangePairs = coChangePairs,
            OrphanedReferences = orphanedReferences,
            SystemReferences = lineage.SystemReferences,
            LinkedServerReferences = lineage.LinkedServerReferences,
        };
    }

    private async Task<CatalogNode?> TryLoadNodeAsync(string objectsRoot, string filePath, CancellationToken cancellationToken)
    {
        string relative = Path.GetRelativePath(objectsRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
        string[] segments = relative.Split('/');
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        ParsedObjectFile parsed = ExtractedObjectFile.Parse(lines);
        if (segments.Length < 3 || (parsed.Identity is null && segments.Length > 5))
        {
            logger.LogWarning("Skipping unexpected path shape: {Relative}", relative);
            return null;
        }

        string server = segments[0];
        string database = segments.Length == 3 ? "_ServerLevel" : segments[1];
        string type = segments.Length == 3 ? segments[1] : segments[2];
        string name = Path.GetFileNameWithoutExtension(segments[^1]);
        string? schema = segments.Length == 5 ? segments[3] : null;

        if (segments.Length == 5 && lines.TakeWhile(line => line.StartsWith("-- ", StringComparison.Ordinal)).Contains("-- Path layout: schema/type"))
        {
            schema = segments[2];
            type = segments[3];
        }
        if (parsed.Identity is { } identity)
        {
            server = ExtractedObjectFile.SafeFileName(identity.Server);
            database = ExtractedObjectFile.SafeFileName(identity.Database);
            schema = identity.Schema is null ? null : ExtractedObjectFile.SafeFileName(identity.Schema);
            type = ExtractedObjectFile.SafeFileName(identity.Type);
            name = ExtractedObjectFile.SafeFileName(identity.Name);
        }
        string id = ExtractedObjectFile.ObjectId(server, database, schema, type, name);
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
            ServiceBrokerGuid = parsed.Identity?.ServiceBrokerGuid,
            ServerIdentity = parsed.Identity?.ServerIdentity,
            Link = parsed.Identity?.Link,
            Principal = parsed.Identity?.Principal,
            SizeBytes = Encoding.UTF8.GetByteCount(parsed.Ddl),
        };
    }

    /// <summary>What one pass of lineage inference produced, beyond the edges themselves.</summary>
    private sealed record LineageInferenceResult
    {
        public required List<CatalogEdge> Edges { get; init; }

        public required List<CatalogOrphanedReference> OrphanedReferences { get; init; }

        public required List<CatalogLinkedServerReference> LinkedServerReferences { get; init; }

        public required List<CatalogSystemReference> SystemReferences { get; init; }
    }

    private LineageInferenceResult InferLineage(List<CatalogNode> nodes, IReadOnlyDictionary<string, CatalogNode> nodesById,
        NodeIndex nodeIndex, LineageAnalysisOptions analysisOptions, IProgress<CatalogProgress>? progress, CancellationToken cancellationToken)
    {
        // Index rather than a set: an edge already recorded from a dynamic reference has to be
        // upgradeable when a static one turns up for the same pair.
        Dictionary<string, int> edgeIndexByKey = [];
        List<CatalogEdge> edges = [];
        // Retain only resolved column tags, not every object's raw references and alias bindings.
        // Tags are applied after inference because another object can add an edge from a link node.
        Dictionary<(string From, string To), IReadOnlyList<string>> columnsByEdge = [];
        HashSet<string> orphanKeys = [];
        List<CatalogOrphanedReference> orphanedReferences = [];
        Dictionary<string, int> linkedReferenceIndexByKey = [];
        List<CatalogLinkedServerReference> linkedServerReferences = [];
        HashSet<string> systemKeys = [];
        List<CatalogSystemReference> systemReferences = [];

        // Resolve synonym definitions before consumers, regardless of file order. Retain
        // only these small analyses and release them as the normal inference pass runs.
        Dictionary<string, LineageAnalysisResult> synonymAnalyses = [];
        Dictionary<string, string> synonymTargets = [];
        foreach (CatalogNode synonym in nodes.Where(n => n.Type == "Synonyms" && n.Engine is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Resolving synonyms", Current: synonym.Id));
            LineageAnalysisResult analysis = lineageAnalyzerResolver.Resolve(synonym.Engine!.Value)
                .Analyze(synonym.Ddl, analysisOptions with { ServiceBrokerGuid = synonym.ServiceBrokerGuid, SourceObjectId = synonym.Id });
            synonymAnalyses[synonym.Id] = analysis;
            if (analysis.ObjectRefs.Count == 1 && nodeIndex.Resolve(synonym, analysis.ObjectRefs[0])
                is { Kind: ReferenceResolutionKind.Resolved, NodeId: { } target })
            {
                synonymTargets[synonym.Id] = target;
            }
        }

        foreach (CatalogNode spec in nodes.Where(n => n.Engine == DatabaseEngine.Oracle && n.Type == "Packages"))
        {
            CatalogNode? body = nodes.FirstOrDefault(n => n.Type == "PackageBodies"
                && string.Equals(n.Server, spec.Server, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Database, spec.Database, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Schema, spec.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
            if (body is not null)
            {
                AddEdge(spec.Id, body.Id, false);
            }
        }

        for (int nodeNumber = 0; nodeNumber < nodes.Count; nodeNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogNode node = nodes[nodeNumber];
            progress?.Report(new("Inferring lineage", nodeNumber, nodes.Count, node.Id, Nodes: nodes.Count, Edges: edges.Count));
            // Database-link connection metadata is already handled by LinkedServerMap.
            // Its DDL declares a connection, not object dependencies, and older exports
            // may contain incomplete credential clauses that cannot be parsed as SQL.
            if (node.Type == "DatabaseLinks" || node.Engine is not { } engine)
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
            logger.LogDebug("Analyzing lineage for {ObjectId}", node.Id);
            LineageAnalysisResult analysis = synonymAnalyses.Remove(node.Id, out LineageAnalysisResult? synonymAnalysis)
                ? synonymAnalysis : analyzer.Analyze(scanText, analysisOptions with { ServiceBrokerGuid = node.ServiceBrokerGuid, SourceObjectId = node.Id });
            cancellationToken.ThrowIfCancellationRequested();
            CollectColumnReferences(node, analysis, nodesById, nodeIndex, synonymTargets, columnsByEdge);

            foreach (ObjectRef reference in analysis.ObjectRefs)
            {
                ReferenceResolution resolution = nodeIndex.Resolve(node, reference);
                bool dynamic = reference.Origin == ReferenceOrigin.Dynamic;

                // Something the engine ships rather than something anybody extracted. No edge (an edge to
                // "the database itself" says nothing) and emphatically no orphan - it is recorded on its
                // own so the object's page can still show what built-ins it leans on.
                if (resolution.Kind == ReferenceResolutionKind.System)
                {
                    if (systemKeys.Add($"{node.Id}|{reference.Server}|{reference.Database}|{reference.Schema}|{reference.Name}"))
                    {
                        systemReferences.Add(new CatalogSystemReference
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

                // A reference that crossed a linked server / database link is recorded against that
                // link whether or not the target itself is extracted, so the link object can list
                // everything reached through it - including the parts of the fleet nobody extracts.
                if (resolution.ViaLink is { } link)
                {
                    string targetKey = resolution.NodeId is { } resolvedId ? $"resolved:{resolvedId}"
                        : $"unresolved:{reference.Database ?? link.DefaultDatabase}|{reference.Schema}|{reference.Name}";
                    string linkedKey = $"{node.Id}|{link.NodeId}|{targetKey}";
                    if (linkedReferenceIndexByKey.TryGetValue(linkedKey, out int existingLinked))
                    {
                        if (!dynamic && linkedServerReferences[existingLinked].Dynamic)
                        {
                            linkedServerReferences[existingLinked] = linkedServerReferences[existingLinked] with { Dynamic = false };
                        }
                    }
                    else
                    {
                        linkedReferenceIndexByKey[linkedKey] = linkedServerReferences.Count;
                        linkedServerReferences.Add(new CatalogLinkedServerReference
                        {
                            LinkedServer = link.NodeId,
                            From = node.Id,
                            To = resolution.NodeId,
                            Database = reference.Database ?? link.DefaultDatabase,
                            Schema = reference.Schema,
                            Name = reference.Name,
                            Dynamic = dynamic,
                            Status = resolution.Kind == ReferenceResolutionKind.NotFound ? "not-observed" : resolution.Kind.ToString().ToLowerInvariant(),
                            TargetServer = link.TargetServer,
                            DataSource = link.Metadata.DataSource ?? link.DataSource,
                            TargetEngine = link.Metadata.TargetEngine,
                        });
                    }

                    // The link itself becomes a hop in the graph: the caller depends on the link, and
                    // the link on whatever it reaches. Drawing it that way (rather than one direct edge
                    // to the remote object) is what makes "which objects go through this linked server"
                    // answerable from the lineage graph, and keeps a hop out of the catalog's scope
                    // visible instead of silently absent.
                    AddEdge(node.Id, link.NodeId, dynamic);
                    if (resolution.NodeId is { } linkedTargetId)
                    {
                        AddEdge(link.NodeId, linkedTargetId, dynamic);
                        if (ResolveSynonymTarget(linkedTargetId, nodesById, synonymTargets) is { } underlyingLinkedId && underlyingLinkedId != linkedTargetId)
                        {
                            AddEdge(node.Id, underlyingLinkedId, dynamic);
                        }
                    }

                    // Remote inventories may be partial or collected with different credentials.
                    // Keep the observed route and status without declaring the remote object dropped.
                    continue;
                }

                if (resolution.Kind == ReferenceResolutionKind.NotFound)
                {
                    // A name recovered from SQL that only exists as a string is a good enough signal to
                    // draw an edge with when it resolves, and nowhere near good enough to accuse anybody
                    // with when it doesn't: the text may be assembled from values this analysis cannot
                    // know. Reporting those as dangling would flood the one list that is meant to be
                    // actionable, so a dynamic miss is simply dropped.
                    if (dynamic)
                    {
                        continue;
                    }

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

                if (resolution is not { Kind: ReferenceResolutionKind.Resolved, NodeId: { } targetId })
                {
                    continue;
                }

                AddEdge(node.Id, targetId, dynamic);
                if (ResolveSynonymTarget(targetId, nodesById, synonymTargets) is { } underlyingId && underlyingId != targetId)
                {
                    AddEdge(node.Id, underlyingId, dynamic);
                }
            }
        }

        progress?.Report(new("Inferring lineage", nodes.Count, nodes.Count, Nodes: nodes.Count, Edges: edges.Count));
        progress?.Report(new("Resolving column references", Total: edges.Count, Unit: "edges"));
        for (int i = 0; i < edges.Count; i++)
        {
            if (columnsByEdge.TryGetValue((edges[i].From, edges[i].To), out IReadOnlyList<string>? columns))
            {
                edges[i] = edges[i] with { Columns = columns };
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

        systemReferences.Sort((a, b) =>
        {
            int byFrom = string.Compare(a.From, b.From, StringComparison.OrdinalIgnoreCase);
            return byFrom != 0 ? byFrom : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new LineageInferenceResult
        {
            Edges = edges,
            OrphanedReferences = orphanedReferences,
            LinkedServerReferences = linkedServerReferences,
            SystemReferences = systemReferences,
        };

        void AddEdge(string from, string to, bool dynamic)
        {
            if (from == to)
            {
                return;
            }

            string key = $"{from}|{to}";
            if (edgeIndexByKey.TryGetValue(key, out int existing))
            {
                // Static beats dynamic: once anything has read this relationship off real DDL, the edge
                // stops being a best-effort finding, however it was first discovered.
                if (!dynamic && edges[existing].Dynamic)
                {
                    edges[existing] = edges[existing] with { Dynamic = false };
                }
                return;
            }

            edgeIndexByKey[key] = edges.Count;
            edges.Add(new CatalogEdge { From = from, To = to, Dynamic = dynamic });
        }
    }

    private static string? ResolveSynonymTarget(string id, IReadOnlyDictionary<string, CatalogNode> nodesById,
        IReadOnlyDictionary<string, string> synonymTargets)
    {
        HashSet<string>? visited = null;
        while (nodesById[id].Type == "Synonyms")
        {
            visited ??= [];
            if (!visited.Add(id) || !synonymTargets.TryGetValue(id, out string? target)) { return null; }
            id = target;
        }
        return id;
    }

    private static void CollectColumnReferences(
        CatalogNode fromNode,
        LineageAnalysisResult analysis,
        IReadOnlyDictionary<string, CatalogNode> nodesById,
        NodeIndex nodeIndex,
        IReadOnlyDictionary<string, string> synonymTargets,
        Dictionary<(string From, string To), IReadOnlyList<string>> columnsByEdge)
    {
        if (analysis.ColumnRefs.Count == 0 || analysis.Aliases.Count == 0)
        {
            return;
        }

        Dictionary<string, string> targetsByAlias = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> targetColumns = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string alias, ObjectRef target) in analysis.Aliases)
        {
            if (nodeIndex.Resolve(fromNode, target) is { Kind: ReferenceResolutionKind.Resolved, NodeId: { } referenceId }
                && ResolveSynonymTarget(referenceId, nodesById, synonymTargets) is { } targetId
                && nodesById.TryGetValue(targetId, out CatalogNode? toNode) && toNode.Columns.Count > 0)
            {
                targetsByAlias[alias] = targetId;
                if (!targetColumns.ContainsKey(targetId))
                {
                    targetColumns[targetId] = new(toNode.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        Dictionary<string, HashSet<string>> usedColumns = new(StringComparer.OrdinalIgnoreCase);
        foreach (ColumnRef columnRef in analysis.ColumnRefs)
        {
            if (targetsByAlias.TryGetValue(columnRef.AliasOrTable, out string? targetId)
                && targetColumns[targetId].Contains(columnRef.Column))
            {
                if (!usedColumns.TryGetValue(targetId, out HashSet<string>? columns))
                {
                    columns = new(StringComparer.OrdinalIgnoreCase);
                    usedColumns[targetId] = columns;
                }
                columns.Add(columnRef.Column);
            }
        }

        foreach ((string targetId, HashSet<string> columns) in usedColumns)
        {
            columnsByEdge[(fromNode.Id, targetId)] = [.. columns.Order(StringComparer.OrdinalIgnoreCase)];
        }
    }
}
