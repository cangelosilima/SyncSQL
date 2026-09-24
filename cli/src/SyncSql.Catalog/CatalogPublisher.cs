using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Catalog;

/// <summary>Native publisher for the site's syncsql-partitioned v1 contract; no Node.js runtime required.</summary>
public sealed partial class CatalogPublisher : ICatalogPublisher
{
    private const int MaxNodes = 1024;
    private const int MaxBytes = 2 * 1024 * 1024;

    public async Task PublishAsync(Core.Domain.Catalog catalog, string manifestPath, bool prune, CancellationToken cancellationToken)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        string output = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(Path.Combine(output, "_catalog"));
        HashSet<string> active = new(StringComparer.Ordinal);
        Dictionary<string, int> routes = new(StringComparer.Ordinal);
        List<List<CatalogNode>> groups = [];
        foreach (var scope in catalog.Nodes.GroupBy(node => (node.Server, node.Database)))
        {
            List<CatalogNode> chunk = [];
            int bytes = 0;
            foreach (CatalogNode node in scope)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int size = JsonSerializer.SerializeToUtf8Bytes(node, SyncSqlJsonOptions.Default).Length;
                if (chunk.Count > 0 && (chunk.Count >= MaxNodes || bytes + size > MaxBytes))
                {
                    groups.Add(chunk);
                    chunk = [];
                    bytes = 0;
                }
                if (!routes.TryAdd(node.Id, groups.Count))
                {
                    throw new InvalidDataException($"Duplicate object id: {node.Id}");
                }
                chunk.Add(node);
                bytes += size;
            }
            if (chunk.Count > 0)
            {
                groups.Add(chunk);
            }
        }

        List<CatalogEdge>[] edges = [.. groups.Select(_ => new List<CatalogEdge>())];
        List<CatalogOrphanedReference>[] orphans = [.. groups.Select(_ => new List<CatalogOrphanedReference>())];
        List<CatalogSystemReference>[] systems = [.. groups.Select(_ => new List<CatalogSystemReference>())];
        List<CatalogLinkedServerReference>[] links = [.. groups.Select(_ => new List<CatalogLinkedServerReference>())];
        Dictionary<string, List<string>> incoming = catalog.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        Dictionary<string, int> outgoing = new(StringComparer.Ordinal);
        Dictionary<string, CatalogNode> nodesById = catalog.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (CatalogEdge edge in catalog.Edges)
        {
            if (!routes.ContainsKey(edge.From) || !routes.ContainsKey(edge.To))
            {
                throw new InvalidDataException($"Edge refers to an unknown object: {edge.From} -> {edge.To}");
            }
            incoming[edge.To].Add(edge.From);
            outgoing[edge.From] = outgoing.GetValueOrDefault(edge.From) + 1;
            Route(edges, edge, edge.From, edge.To);
        }
        foreach (var reference in catalog.OrphanedReferences)
        {
            Route(orphans, reference, reference.From);
        }
        foreach (var reference in catalog.SystemReferences)
        {
            Route(systems, reference, reference.From);
        }
        foreach (var reference in catalog.LinkedServerReferences)
        {
            Route(links, reference, reference.From, reference.To, reference.LinkedServer);
        }
        var targetEngines = catalog.LinkedServerReferences
            .Where(reference => reference.To is not null && nodesById.GetValueOrDefault(reference.To)?.Engine is not null)
            .GroupBy(reference => reference.LinkedServer)
            .ToDictionary(group => group.Key, group => group.Select(reference => nodesById[reference.To!].Engine!.Value).Distinct().Order().ToArray(), StringComparer.Ordinal);

        List<object> partitions = [];
        List<object> summaries = [];
        List<JsonObject> summaryPage = [];
        int summaryBytes = 0;
        (string Server, string Database) summaryScope = (string.Empty, string.Empty);
        for (int part = 0; part < groups.Count; part++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<CatalogNode> nodes = groups[part];
            JsonArray details = [];
            JsonArray summaryNodes = [];
            foreach (CatalogNode node in nodes)
            {
                JsonObject detail = JsonSerializer.SerializeToNode(node, SyncSqlJsonOptions.Default)!.AsObject();
                Remove(detail, "ddl", "sections", "grants");
                details.Add(detail);
                JsonObject summary = (JsonObject)detail.DeepClone();
                Remove(summary, "history", "columns", "metrics");
                summary["columnNames"] = JsonSerializer.SerializeToNode(node.Columns.Select(column => column.Name));
                summary["columnCount"] = node.Columns.Count;
                summary["grantCount"] = node.Grants.Count;
                summary["granteeNames"] = JsonSerializer.SerializeToNode(node.Grants.Select(grant => grant.Grantee).Distinct());
                summary["metrics"] = JsonSerializer.SerializeToNode((node.Metrics.Count >= 2 ? node.Metrics.TakeLast(2) : []).Select(metric => new
                {
                    capturedAt = metric.CapturedAt,
                    rowCount = metric.RowCount,
                    reservedKB = (long?)null,
                    dataKB = (long?)null,
                    indexKB = (long?)null,
                    indexes = metric.Indexes.Select(index => new { name = index.Name, fragmentationPct = index.FragmentationPct }),
                    statistics = Array.Empty<object>(),
                }));
                summary["dependsOnCount"] = outgoing.GetValueOrDefault(node.Id);
                summary["usedByCount"] = incoming[node.Id].Count;
                if (targetEngines.TryGetValue(node.Id, out DatabaseEngine[]? engines))
                {
                    summary["linkTargetEngines"] = JsonSerializer.SerializeToNode(engines, SyncSqlJsonOptions.Default);
                }
                summaryNodes.Add(summary);
            }
            partitions.Add(new
            {
                server = nodes[0].Server,
                database = nodes[0].Database,
                count = nodes.Count,
                details = await WritePart(details),
                search = await WritePart(nodes.Select(node => new { id = node.Id, ddl = node.Ddl, sections = node.Sections })),
                grants = await WritePart(nodes.Where(node => node.Grants.Count > 0).Select(node => new { id = node.Id, grants = node.Grants })),
                edges = await WritePart(new { edges = edges[part], orphanedReferences = orphans[part], systemReferences = systems[part], linkedServerReferences = links[part] }),
                orphanedReferenceCount = orphans[part].Count,
            });
            JsonObject summaryGroup = new() { ["partition"] = part, ["nodes"] = summaryNodes };
            int size = JsonSerializer.SerializeToUtf8Bytes(summaryGroup).Length;
            var scope = (nodes[0].Server, nodes[0].Database);
            if (summaryPage.Count > 0 && (summaryScope != scope || summaryBytes + size > MaxBytes))
            {
                await FlushSummaries();
            }
            summaryScope = scope;
            summaryPage.Add(summaryGroup);
            summaryBytes += size;
        }
        await FlushSummaries();

        // Same six-hop, stop-after-crossing-server ranking used by the site's overview.
        var ranked = catalog.Nodes.Where(node => node.Type == "Tables" && incoming[node.Id].Count > 0)
            .OrderByDescending(node => incoming[node.Id].Count).ToList();
        int threshold = ranked.Count > 0 ? incoming[ranked[Math.Min(9, ranked.Count - 1)].Id].Count : 0;
        var topReferencedTables = ranked.Where(node => incoming[node.Id].Count >= threshold).Select(node =>
        {
            HashSet<string> visited = [node.Id];
            List<string> frontier = [node.Id];
            for (int hop = 0; hop < 6 && frontier.Count > 0; hop++)
            {
                List<string> next = [];
                foreach (string current in frontier)
                {
                    foreach (string previous in incoming[current])
                    {
                        if (visited.Add(previous) && nodesById[current].Server == nodesById[previous].Server)
                        {
                            next.Add(previous);
                        }
                    }
                }
                frontier = next;
            }
            return new { id = node.Id, directUsers = incoming[node.Id].Count, indirectUsers = visited.Count - 1 };
        }).OrderByDescending(row => row.directUsers).ThenByDescending(row => row.indirectUsers).Take(10).ToArray();
        var manifest = new
        {
            format = "syncsql-partitioned",
            version = 1,
            generatedAt = catalog.GeneratedAt,
            servers = catalog.Servers,
            serverDetails = catalog.ServerDetails,
            typeCounts = catalog.TypeCounts,
            recentChanges = catalog.RecentChanges,
            coChangePairs = catalog.CoChangePairs,
            nodeCount = catalog.Nodes.Count,
            edgeCount = catalog.Edges.Count,
            orphanedReferenceCount = catalog.OrphanedReferences.Count,
            topReferencedTables,
            summaries,
            partitions,
        };
        await WriteAtomically(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, SyncSqlJsonOptions.Default), cancellationToken);
        if (prune)
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(output, "_catalog")))
            {
                string relative = "_catalog/" + Path.GetFileName(file);
                if (PayloadPath().IsMatch(relative) && !active.Contains(relative))
                {
                    File.Delete(file);
                }
            }
        }

        void Route<T>(List<T>[] buckets, T value, params string?[] ids)
        {
            foreach (int part in ids.OfType<string>().Where(routes.ContainsKey).Select(id => routes[id]).Distinct())
            {
                buckets[part].Add(value);
            }
        }

        async Task<string> WritePart<T>(T value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, SyncSqlJsonOptions.Default);
            string relative = $"_catalog/{Convert.ToHexStringLower(SHA256.HashData(json))}.json.gz";
            active.Add(relative);
            string destination = Path.Combine(output, relative);
            // Always replace atomically, including any incomplete payload from an interrupted older run.
            using MemoryStream compressed = new();
            using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                // Compression into a MemoryStream is CPU work; only the file write needs asynchronous I/O.
                gzip.Write(json);
            }
            await WriteAtomically(destination, compressed.ToArray(), cancellationToken);
            return relative;
        }

        async Task FlushSummaries()
        {
            if (summaryPage.Count == 0)
            {
                return;
            }
            summaries.Add(new { file = await WritePart(summaryPage), count = summaryPage.Sum(group => group["nodes"]!.AsArray().Count) });
            summaryPage.Clear();
            summaryBytes = 0;
        }
    }

    private static void Remove(JsonObject value, params string[] fields)
    {
        foreach (string field in fields)
        {
            value.Remove(field);
        }
    }

    private static async Task WriteAtomically(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [GeneratedRegex(@"^_catalog/[a-f0-9]{64}\.json(?:\.gz)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PayloadPath();
}
