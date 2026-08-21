using SyncSql.Core.Domain;

namespace SyncSql.Catalog;

/// <summary>
/// Resolves a (possibly schema-qualified) <see cref="ObjectRef"/> found in one node's DDL to the node
/// id it refers to, scoped to that node's own server+database (or server, for a bare cross-linked-
/// server/DB-link reference) - a direct port of Build-Catalog.ps1's qualifiedIndex/bareIndexDb/
/// bareIndexServer + Resolve-SyncSqlObjectRef.
/// </summary>
internal sealed class NodeIndex
{
    private readonly Dictionary<string, string> _qualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _bareInDatabase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _bareOnServer = new(StringComparer.OrdinalIgnoreCase);

    public NodeIndex(IEnumerable<CatalogNode> nodes)
    {
        foreach (CatalogNode node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Schema))
            {
                _qualified[$"{node.Server}::{node.Database}::{node.Schema}.{node.Name}"] = node.Id;
            }

            Add(_bareInDatabase, $"{node.Server}::{node.Database}::{node.Name}", node.Id);
            Add(_bareOnServer, $"{node.Server}::{node.Name}", node.Id);
        }

        static void Add(Dictionary<string, List<string>> index, string key, string nodeId)
        {
            if (!index.TryGetValue(key, out List<string>? list))
            {
                list = [];
                index[key] = list;
            }
            list.Add(nodeId);
        }
    }

    /// <summary>
    /// Resolves within $fromNode's own server+database scope. A schema-qualified reference to an
    /// unknown schema is left unresolved rather than falling back to a bare-name guess - if the DDL was
    /// specific, an ambiguous bare match would be a worse guess, not a better one. A bare reference only
    /// resolves when exactly one object with that name exists in scope (database first, then server,
    /// for cross-linked-server/DB-link bare references).
    /// </summary>
    public string? Resolve(CatalogNode fromNode, ObjectRef reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Name))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(reference.Schema))
        {
            return _qualified.TryGetValue($"{fromNode.Server}::{fromNode.Database}::{reference.Schema}.{reference.Name}", out string? id) ? id : null;
        }

        if (_bareInDatabase.TryGetValue($"{fromNode.Server}::{fromNode.Database}::{reference.Name}", out List<string>? inDatabase) && inDatabase.Count == 1)
        {
            return inDatabase[0];
        }

        if (_bareOnServer.TryGetValue($"{fromNode.Server}::{reference.Name}", out List<string>? onServer) && onServer.Count == 1)
        {
            return onServer[0];
        }

        return null;
    }
}
