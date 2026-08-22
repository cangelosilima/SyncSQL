using SyncSql.Core.Domain;

namespace SyncSql.Catalog;

/// <summary>Why a <see cref="NodeIndex.Resolve"/> lookup did or didn't produce a node id.</summary>
internal enum ReferenceResolutionKind
{
    /// <summary>Exactly one node matched - see <see cref="ReferenceResolution.NodeId"/>.</summary>
    Resolved,

    /// <summary>No node anywhere in scope has this name - a candidate orphaned/dangling reference.</summary>
    NotFound,

    /// <summary>More than one node in scope shares this bare name - genuinely ambiguous, not "missing".</summary>
    Ambiguous,
}

/// <summary>The outcome of one <see cref="NodeIndex.Resolve"/> lookup.</summary>
internal readonly record struct ReferenceResolution(ReferenceResolutionKind Kind, string? NodeId)
{
    public static ReferenceResolution Found(string nodeId) => new(ReferenceResolutionKind.Resolved, nodeId);

    public static readonly ReferenceResolution NotFound = new(ReferenceResolutionKind.NotFound, null);

    public static readonly ReferenceResolution Ambiguous = new(ReferenceResolutionKind.Ambiguous, null);
}

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
    /// for cross-linked-server/DB-link bare references); more than one is reported as
    /// <see cref="ReferenceResolutionKind.Ambiguous"/> rather than silently guessed at, and is distinct
    /// from <see cref="ReferenceResolutionKind.NotFound"/> (no candidate at all) precisely so callers can
    /// tell "this looks like a dropped/renamed object" apart from "this name is inherently ambiguous
    /// here" - see <see cref="CatalogBuilder"/>'s orphaned-reference detection.
    /// </summary>
    public ReferenceResolution Resolve(CatalogNode fromNode, ObjectRef reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Name))
        {
            return ReferenceResolution.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(reference.Schema))
        {
            return _qualified.TryGetValue($"{fromNode.Server}::{fromNode.Database}::{reference.Schema}.{reference.Name}", out string? qualifiedId)
                ? ReferenceResolution.Found(qualifiedId)
                : ReferenceResolution.NotFound;
        }

        if (_bareInDatabase.TryGetValue($"{fromNode.Server}::{fromNode.Database}::{reference.Name}", out List<string>? inDatabase))
        {
            return inDatabase.Count switch
            {
                1 => ReferenceResolution.Found(inDatabase[0]),
                > 1 => ReferenceResolution.Ambiguous,
                _ => ReferenceResolution.NotFound,
            };
        }

        if (_bareOnServer.TryGetValue($"{fromNode.Server}::{reference.Name}", out List<string>? onServer))
        {
            return onServer.Count switch
            {
                1 => ReferenceResolution.Found(onServer[0]),
                > 1 => ReferenceResolution.Ambiguous,
                _ => ReferenceResolution.NotFound,
            };
        }

        return ReferenceResolution.NotFound;
    }
}
