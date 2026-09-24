using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SyncSql.Core.Json;

namespace SyncSql.Tests;

/// <summary>Reassembles the published format for assertions against the real CLI output.</summary>
internal static class PartitionedCatalogReader
{
    public static async Task<Core.Domain.Catalog> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken))!.AsObject();
        if (manifest["format"]?.GetValue<string>() != "syncsql-partitioned" || manifest["version"]?.GetValue<int>() != 1)
        {
            throw new InvalidDataException("Expected a partitioned catalog v1 manifest.");
        }
        JsonArray nodes = [];
        Dictionary<string, JsonNode> edges = new(StringComparer.Ordinal);
        Dictionary<string, JsonNode> orphans = new(StringComparer.Ordinal);
        Dictionary<string, JsonNode> systems = new(StringComparer.Ordinal);
        Dictionary<string, JsonNode> links = new(StringComparer.Ordinal);
        foreach (JsonNode part in manifest["partitions"]!.AsArray().OfType<JsonNode>())
        {
            JsonArray details = (await ReadPart(part["details"]!)).AsArray();
            var search = (await ReadPart(part["search"]!)).AsArray().OfType<JsonObject>().ToDictionary(node => node["id"]!.GetValue<string>(), StringComparer.Ordinal);
            var grants = (await ReadPart(part["grants"]!)).AsArray().OfType<JsonObject>().ToDictionary(node => node["id"]!.GetValue<string>(), StringComparer.Ordinal);
            foreach (JsonObject detail in details.OfType<JsonObject>())
            {
                string id = detail["id"]!.GetValue<string>();
                JsonObject node = (JsonObject)detail.DeepClone();
                node["ddl"] = search[id]["ddl"]!.DeepClone();
                node["sections"] = search[id]["sections"]!.DeepClone();
                node["grants"] = grants.GetValueOrDefault(id)?["grants"]!.DeepClone() ?? new JsonArray();
                nodes.Add(node);
            }
            JsonNode graph = await ReadPart(part["edges"]!);
            Add(edges, graph["edges"]!);
            Add(orphans, graph["orphanedReferences"]!);
            Add(systems, graph["systemReferences"]!);
            Add(links, graph["linkedServerReferences"]!);
        }
        manifest["nodes"] = nodes;
        manifest["edges"] = new JsonArray([.. edges.Values]);
        manifest["orphanedReferences"] = new JsonArray([.. orphans.Values]);
        manifest["systemReferences"] = new JsonArray([.. systems.Values]);
        manifest["linkedServerReferences"] = new JsonArray([.. links.Values]);
        return manifest.Deserialize<Core.Domain.Catalog>(SyncSqlJsonOptions.Default)!;

        async Task<JsonNode> ReadPart(JsonNode file)
        {
            string relative = file.GetValue<string>();
            if (!Regex.IsMatch(relative, @"^_catalog/[a-f0-9]{64}\.json\.gz$", RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException($"Invalid partition path: {relative}");
            }
            await using FileStream stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, relative));
            await using GZipStream gzip = new(stream, CompressionMode.Decompress);
            return (await JsonNode.ParseAsync(gzip, cancellationToken: cancellationToken))!;
        }
    }

    private static void Add(Dictionary<string, JsonNode> destination, JsonNode values)
    {
        foreach (JsonNode value in values.AsArray().OfType<JsonNode>())
        {
            destination.TryAdd(value.ToJsonString(), value.DeepClone());
        }
    }
}
