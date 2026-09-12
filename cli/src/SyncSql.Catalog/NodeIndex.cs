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

    /// <summary>
    /// The reference names a place the catalog doesn't cover - a linked server nothing in the catalog
    /// answers to, or a database that isn't extracted on the server it does answer to. Distinct from
    /// <see cref="NotFound"/> on purpose: nothing is dangling, the target is simply outside what was
    /// extracted, so it must not be reported as an orphan.
    /// </summary>
    External,

    /// <summary>
    /// The reference names something the engine itself provides - <c>sp_executesql</c>, <c>sys.objects</c>,
    /// <c>DBMS_OUTPUT</c>. Also not dangling and also not an orphan, but distinct from
    /// <see cref="External"/> because the reason is different and worth showing: this target isn't
    /// "somewhere we don't extract", it's built into the database.
    /// </summary>
    System,
}

/// <summary>The outcome of one <see cref="NodeIndex.Resolve"/> lookup.</summary>
/// <param name="Kind">What the lookup concluded.</param>
/// <param name="NodeId">The single node that matched, for <see cref="ReferenceResolutionKind.Resolved"/> only.</param>
/// <param name="ViaLink">The linked server / database link the lookup crossed to get there, when it crossed one.</param>
internal readonly record struct ReferenceResolution(ReferenceResolutionKind Kind, string? NodeId, LinkedServerLink? ViaLink = null)
{
    public static ReferenceResolution Found(string nodeId, LinkedServerLink? viaLink = null) => new(ReferenceResolutionKind.Resolved, nodeId, viaLink);

    public static ReferenceResolution External(LinkedServerLink? viaLink = null) => new(ReferenceResolutionKind.External, null, viaLink);

    public static readonly ReferenceResolution NotFound = new(ReferenceResolutionKind.NotFound, null);

    public static readonly ReferenceResolution Ambiguous = new(ReferenceResolutionKind.Ambiguous, null);

    public static readonly ReferenceResolution System = new(ReferenceResolutionKind.System, null);
}

/// <summary>
/// Resolves a (possibly qualified) <see cref="ObjectRef"/> found in one node's DDL to the node id it
/// refers to.
///
/// The lookup starts in the referencing node's own server+database and widens from there, in the order
/// a reader of the DDL would: the reference's own qualifiers first (a 3-part "OtherDb.dbo.Orders" or a
/// 4-part "LNK.OtherDb.dbo.Orders" says exactly where to look), then the rest of the databases on the
/// same server, then the servers reachable from it through a linked server / database link. Widening
/// only ever settles on a *unique* match - two candidates are reported as
/// <see cref="ReferenceResolutionKind.Ambiguous"/> rather than guessed at - and a reference that points
/// somewhere the catalog doesn't cover at all comes back as <see cref="ReferenceResolutionKind.External"/>,
/// so it isn't mistaken for a dropped object. That distinction is what keeps
/// <see cref="CatalogBuilder"/>'s orphaned-reference list about genuinely dangling references.
/// </summary>
internal sealed class NodeIndex
{
    private readonly Dictionary<string, string> _qualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _qualifiedOnServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _bareInDatabase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _bareOnServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _databasesByServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedServerMap _linkedServers;
    private readonly Dictionary<string, string> _typedInDatabase = new(StringComparer.Ordinal);

    private static string BrokerKey(string server, string database, string type, string? schema, string name) =>
        $"{server.ToUpperInvariant()}::{database.ToUpperInvariant()}::{type.ToUpperInvariant()}::{schema?.ToUpperInvariant()}."
        + (type == "Queues" ? name.ToUpperInvariant() : name);
    private readonly Dictionary<string, DatabaseEngine?> _enginesByServer = new(StringComparer.OrdinalIgnoreCase);

    public NodeIndex(IEnumerable<CatalogNode> nodes)
        : this(nodes, LinkedServerMap.Empty)
    {
    }

    public NodeIndex(IEnumerable<CatalogNode> nodes, LinkedServerMap linkedServers)
    {
        _linkedServers = linkedServers;

        List<CatalogNode> allNodes = [.. nodes];
        HashSet<string> packageSpecs = allNodes.Where(n => n.Type == "Packages")
            .Select(n => $"{n.Server}::{n.Database}::{n.Schema}.{n.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogNode node in allNodes)
        {
            if (node.Type is "MessageTypes" or "Contracts" or "Services" or "Queues")
            {
                _typedInDatabase[BrokerKey(node.Server, node.Database, node.Type, node.Schema, node.Name)] = node.Id;
            }
            if (node.Engine is { } engine)
            {
                _enginesByServer.TryAdd(node.Server, engine);
            }
            // Database-scoped Broker names are not candidates for ordinary table/routine references.
            if (node.Type is "MessageTypes" or "Contracts" or "Services")
            {
                continue;
            }
            // Spec and body share an Oracle name. Calls resolve to the public spec;
            // the catalog adds the spec -> implementation dependency separately.
            if (node.Type == "PackageBodies" && packageSpecs.Contains($"{node.Server}::{node.Database}::{node.Schema}.{node.Name}"))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(node.Schema))
            {
                _qualified[$"{node.Server}::{node.Database}::{node.Schema}.{node.Name}"] = node.Id;
                Add(_qualifiedOnServer, $"{node.Server}::{node.Schema}.{node.Name}", node.Id);
            }

            Add(_bareInDatabase, $"{node.Server}::{node.Database}::{node.Name}", node.Id);
            Add(_bareOnServer, $"{node.Server}::{node.Name}", node.Id);

            if (!_databasesByServer.TryGetValue(node.Server, out HashSet<string>? databases))
            {
                databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _databasesByServer[node.Server] = databases;
            }
            databases.Add(node.Database);
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
    /// Resolves one reference made by <paramref name="fromNode"/>.
    ///
    /// A reference that names a linked server / database link is followed to whatever catalog server
    /// that link lands on, defaulting to the database the link itself declares when the reference
    /// doesn't name one; a link nothing in the catalog answers to (or a named database that isn't
    /// extracted) is <see cref="ReferenceResolutionKind.External"/>, not an orphan.
    ///
    /// A schema-qualified reference is looked up in its target database, then - only when the DDL left
    /// the database unsaid, so the "database" was an assumption rather than a statement - across the
    /// other databases on that server, then across the servers one link away. An unknown *schema* is
    /// still never downgraded to a bare-name guess: if the DDL was specific, an ambiguous bare match
    /// would be a worse answer, not a better one. A bare reference resolves only within its own
    /// database and then its own server, as before - a name with no qualifiers at all is too weak a
    /// signal to carry across a server boundary.
    /// </summary>
    public ReferenceResolution Resolve(CatalogNode fromNode, ObjectRef reference)
    {
        // Broker message types, contracts and services have separate database-local namespaces.
        // Never widen these references to a similarly named object in another database.
        if (reference.ObjectType is { } objectType)
        {
            if (reference.Name == "DEFAULT" && objectType is "MessageTypes" or "Contracts")
            {
                return ReferenceResolution.System;
            }
            string? schema = objectType == "Queues" ? reference.Schema ?? fromNode.Schema ?? "dbo" : null;
            return _typedInDatabase.TryGetValue(BrokerKey(fromNode.Server, fromNode.Database, objectType, schema, reference.Name), out string? id)
                ? ReferenceResolution.Found(id) : ReferenceResolution.NotFound;
        }
        if (string.IsNullOrWhiteSpace(reference.Name))
        {
            return ReferenceResolution.NotFound;
        }

        string targetServer = fromNode.Server;
        LinkedServerLink? viaLink = null;

        // "Stated" means the DDL (or the link it crosses) actually named the database, as opposed to one
        // being assumed from where the referencing object itself lives.
        string? statedDatabase = reference.Database;

        // A four-part name that spells out this object's own server isn't a hop - people write the local
        // server's name out in full often enough that treating it as a foreign one would strand a pile of
        // perfectly local references outside the catalog.
        LinkedServerLink? namedLink = !string.IsNullOrWhiteSpace(reference.Server)
            ? _linkedServers.Resolve(fromNode.Server, reference.Server, fromNode.Database, fromNode.Schema) : null;
        if (!string.IsNullOrWhiteSpace(reference.Server)
            && (namedLink is not null || (!string.Equals(reference.Server, fromNode.Server, StringComparison.OrdinalIgnoreCase)
                && !fromNode.ServerNames.Contains(reference.Server, StringComparer.OrdinalIgnoreCase))))
        {
            LinkedServerLink? link = namedLink;
            if (link?.TargetServer is not { } linkedTargetServer)
            {
                // Either the server declares no such link, or the link points somewhere nothing in the
                // catalog answers to - the reference leaves the catalog's scope either way.
                return ReferenceResolution.External(link);
            }

            if (reference.IsRoutine && reference.Database is not null
                && _enginesByServer.GetValueOrDefault(linkedTargetServer) == DatabaseEngine.Oracle)
            {
                // T-SQL's dynamic scanner sees OWNER.PACKAGE.MEMBER as three name
                // parts. On the Oracle destination these are not database.schema.name.
                reference = reference with { Schema = reference.Database, Name = reference.Schema!, Database = null };
                statedDatabase = null;
            }
            statedDatabase ??= link.DefaultDatabase;

            // A loopback link lands right back here: the reference is local, and drawing it through the
            // link node would invent a boundary nothing actually crosses.
            if (!string.Equals(linkedTargetServer, fromNode.Server, StringComparison.OrdinalIgnoreCase))
            {
                targetServer = linkedTargetServer;
                viaLink = link;
            }
        }

        // Crossing a link that pins no database leaves the database open, so the lookup covers the whole
        // server it landed on; staying here, an unstated database means this object's own.
        string? targetDatabase = statedDatabase ?? (viaLink is null ? fromNode.Database : null);

        // A database the reference named (or the link declared) that simply isn't extracted puts the
        // target outside the catalog rather than making it missing. The referencing node's own database
        // is in the catalog by construction, so this only ever fires on a spelled-out qualifier.
        if (targetDatabase is not null && !HasDatabase(targetServer, targetDatabase))
        {
            // "master.dbo.xp_cmdshell" lands here rather than below, because master is usually not
            // extracted. It is still a built-in, and saying so is more useful than "outside the catalog":
            // nothing was ever going to extract it. Nothing in the catalog can be shadowed by this, since
            // by definition no node lives in a database that isn't there.
            return SystemObjectCatalog.IsSystemObject(fromNode.Engine, reference)
                ? ReferenceResolution.System
                : ReferenceResolution.External(viaLink);
        }

        bool databaseWasStated = statedDatabase is not null;

        ReferenceResolution resolution = string.IsNullOrWhiteSpace(reference.Schema)
            ? ResolveBare(reference.Name, targetServer, targetDatabase, viaLink)
            : ResolveQualified(fromNode, reference, targetServer, targetDatabase, databaseWasStated, viaLink);

        // Built-ins are recognized only once the real lookup has come up empty, never before it. That
        // ordering is the whole guard: a user object that happens to use a reserved-looking name - a
        // hand-written dbo.sp_NightlyRollup - is found by the index and returned as itself, and the
        // engine-built-in rules below only ever get to speak for a name nothing in the catalog answers to.
        return resolution.Kind == ReferenceResolutionKind.NotFound && SystemObjectCatalog.IsSystemObject(fromNode.Engine, reference)
            ? ReferenceResolution.System
            : resolution;
    }

    private ReferenceResolution ResolveQualified(
        CatalogNode fromNode,
        ObjectRef reference,
        string targetServer,
        string? targetDatabase,
        bool databaseWasStated,
        LinkedServerLink? viaLink)
    {
        string qualifiedName = $"{reference.Schema}.{reference.Name}";

        if (targetDatabase is not null
            && _qualified.TryGetValue($"{targetServer}::{targetDatabase}::{qualifiedName}", out string? inDatabase))
        {
            return ReferenceResolution.Found(inDatabase, viaLink);
        }

        // Everything below widens past the database the reference was assumed to mean. A reference that
        // spelled its database (or crossed a link that declared one) already said where to look, so
        // there's nothing to widen to: it's missing where it claimed to be.
        if (databaseWasStated)
        {
            return ReferenceResolution.NotFound;
        }

        if (Unique(_qualifiedOnServer, $"{targetServer}::{qualifiedName}") is { } onServer)
        {
            return onServer.Kind == ReferenceResolutionKind.Resolved
                ? ReferenceResolution.Found(onServer.NodeId!, viaLink)
                : onServer;
        }

        // Last resort: the same schema-qualified name on a server one link away. Only a single candidate
        // across all of them counts - the point is to connect a reference the DDL under-qualified, not to
        // pick a winner between two plausible targets. A reference that already named its link has had
        // its one hop; fanning out from there would be inventing a route the DDL didn't take.
        if (viaLink is not null)
        {
            return ReferenceResolution.NotFound;
        }

        string? crossServerMatch = null;
        LinkedServerLink? crossServerLink = null;
        foreach (string linkedServer in _linkedServers.ReachableFrom(fromNode.Server))
        {
            LinkedServerLink[] accessibleLinks = _linkedServers.LinksTo(fromNode.Server, linkedServer, fromNode.Database, fromNode.Schema);
            if (accessibleLinks.Length == 0)
            {
                continue;
            }

            if (Unique(_qualifiedOnServer, $"{linkedServer}::{qualifiedName}") is not { } linked)
            {
                continue;
            }

            if (linked.Kind == ReferenceResolutionKind.Ambiguous || crossServerMatch is not null || accessibleLinks.Length > 1)
            {
                return ReferenceResolution.Ambiguous;
            }

            crossServerMatch = linked.NodeId;
            crossServerLink = accessibleLinks[0];
        }

        return crossServerMatch is not null
            ? ReferenceResolution.Found(crossServerMatch, crossServerLink)
            : ReferenceResolution.NotFound;
    }

    private ReferenceResolution ResolveBare(string name, string targetServer, string? targetDatabase, LinkedServerLink? viaLink)
    {
        if (targetDatabase is not null && Unique(_bareInDatabase, $"{targetServer}::{targetDatabase}::{name}") is { } inDatabase)
        {
            return inDatabase.Kind == ReferenceResolutionKind.Resolved
                ? ReferenceResolution.Found(inDatabase.NodeId!, viaLink)
                : inDatabase;
        }

        if (Unique(_bareOnServer, $"{targetServer}::{name}") is { } onServer)
        {
            return onServer.Kind == ReferenceResolutionKind.Resolved
                ? ReferenceResolution.Found(onServer.NodeId!, viaLink)
                : onServer;
        }

        return ReferenceResolution.NotFound;
    }

    private bool HasDatabase(string server, string database) =>
        _databasesByServer.TryGetValue(server, out HashSet<string>? databases) && databases.Contains(database);

    /// <summary>Resolved when exactly one node is indexed under <paramref name="key"/>, Ambiguous when several, null (keep looking) when none.</summary>
    private static ReferenceResolution? Unique(Dictionary<string, List<string>> index, string key)
    {
        if (!index.TryGetValue(key, out List<string>? matches))
        {
            return null;
        }

        // Entries are created only when adding their first node, so every stored list is nonempty.
        return matches.Count == 1 ? ReferenceResolution.Found(matches[0]) : ReferenceResolution.Ambiguous;
    }
}
