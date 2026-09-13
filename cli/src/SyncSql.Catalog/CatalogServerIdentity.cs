using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;
using SyncSql.Core.Configuration;

namespace SyncSql.Catalog;

internal static class CatalogServerIdentity
{
    /// <summary>Unify endpoint identity before indexing objects, retaining paths for link provenance.</summary>
    public static List<CatalogNode> Canonicalize(List<CatalogNode> nodes)
    {
        ServerIdentityRegistry identities = new(nodes.Select(n => n.ServerIdentity).OfType<ServerIdentity>());
        Dictionary<string, string> endpointByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogNode node in nodes.Where(n => n.ServerIdentity is not null))
        {
            string endpoint = identities.CanonicalKey(node.ServerIdentity!);
            if (endpointByName.TryGetValue(node.Server, out string? existing)
                && !string.Equals(existing, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Server '{node.Server}' has conflicting endpoint metadata at '{node.Path}'. Re-extract into a clean staging tree.");
            }
            endpointByName[node.Server] = endpoint;
        }
        Dictionary<string, string> canonicalByEndpoint = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogNode node in nodes.OrderBy(n => n.Path.StartsWith(n.Server + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Server, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Path, StringComparer.Ordinal))
        {
            if (endpointByName.TryGetValue(node.Server, out string? endpoint))
            {
                canonicalByEndpoint.TryAdd(endpoint, node.Server);
            }
        }
        Dictionary<string, IReadOnlyList<string>> namesByEndpoint = endpointByName.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group.Select(pair => pair.Key)], StringComparer.OrdinalIgnoreCase);
        return [.. nodes.Select(node => endpointByName.TryGetValue(node.Server, out string? endpoint)
            ? node with
            {
                Server = canonicalByEndpoint[endpoint],
                Id = ExtractedObjectFile.ObjectId(canonicalByEndpoint[endpoint], node.Database, node.Schema, node.Type, node.Name),
                ServerNames = namesByEndpoint[endpoint],
            }
            : node)];
    }

    public static List<CatalogNode> MergeObjects(List<CatalogNode> nodes)
    {
        List<CatalogNode> result = [];
        foreach (IGrouping<string, CatalogNode> group in nodes.GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
        {
            CatalogNode first = group.OrderBy(n => n.Path.StartsWith(n.Server + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n.Path, StringComparer.Ordinal).First();
            if (group.Count() > 1 && group.Any(node => Content(node) != Content(first)))
            {
                throw new InvalidDataException($"Conflicting exports for object '{first.Id}': {string.Join(", ", group.Select(n => n.Path))}. Re-extract overlapping scopes from a consistent source before building the catalog.");
            }
            result.Add(first);
        }
        return result;
    }

    private static string Content(CatalogNode node) => JsonSerializer.Serialize(new
    {
        node.Engine,
        node.Ddl,
        node.Description,
        node.Columns,
        node.Grants,
        node.Sections,
        node.ServiceBrokerGuid,
    });
}
