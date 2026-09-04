using System.Text.Json.Serialization;

namespace SyncSql.Core.Domain;

/// <summary>One historical version of an object, mined from git log/show for the point-in-time viewer.</summary>
public sealed record CatalogObjectVersion
{
    [JsonPropertyName("sha")]
    public required string Sha { get; init; }

    [JsonPropertyName("date")]
    public required DateTimeOffset Date { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("ddl")]
    public string? Ddl { get; init; }
}

/// <summary>One catalog node: a single extracted object plus everything Build/mining attaches to it.</summary>
public sealed record CatalogNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("server")]
    public required string Server { get; init; }

    [JsonPropertyName("database")]
    public required string Database { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("qualifiedName")]
    public required string QualifiedName { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("ddl")]
    public required string Ddl { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("columns")]
    public IReadOnlyList<ExtractedColumn> Columns { get; init; } = [];

    [JsonPropertyName("grants")]
    public IReadOnlyList<GrantEntry> Grants { get; init; } = [];

    [JsonPropertyName("sections")]
    public IReadOnlyList<ExtractedSection> Sections { get; init; } = [];

    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("changeCount")]
    public int ChangeCount { get; init; }

    [JsonPropertyName("lastChangedAt")]
    public DateTimeOffset? LastChangedAt { get; init; }

    [JsonPropertyName("history")]
    public IReadOnlyList<CatalogObjectVersion> History { get; init; } = [];

    [JsonPropertyName("metrics")]
    public IReadOnlyList<MetricsSnapshot> Metrics { get; init; } = [];

    /// <summary>
    /// Extra field beyond site/src/types.ts's CatalogNode (the site doesn't need it to render) - which
    /// backend produced this node, used by SyncSql.Catalog to dispatch lineage inference. Null for a
    /// node whose source file predates the "-- Engine:" header.
    /// </summary>
    [JsonPropertyName("engine")]
    public DatabaseEngine? Engine { get; init; }
}

/// <summary>A best-effort inferred (or FK-structural) lineage edge, annotated with any resolved column references.</summary>
public sealed record CatalogEdge
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("columns")]
    public IReadOnlyList<string> Columns { get; init; } = [];
}

/// <summary>One commit that touched at least one catalogued object, for the global History timeline.</summary>
public sealed record CatalogCommit
{
    [JsonPropertyName("sha")]
    public required string Sha { get; init; }

    [JsonPropertyName("date")]
    public required DateTimeOffset Date { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("objectIds")]
    public required IReadOnlyList<string> ObjectIds { get; init; }
}

/// <summary>Two objects that tend to change together in the same commit.</summary>
public sealed record CoChangePair
{
    [JsonPropertyName("a")]
    public required string A { get; init; }

    [JsonPropertyName("b")]
    public required string B { get; init; }

    [JsonPropertyName("count")]
    public required int Count { get; init; }
}

/// <summary>
/// A reference one object's DDL makes to another object that doesn't exist anywhere the lookup can
/// reach - its own database, the other databases on its server, or a server one linked server / database
/// link away - most often because the target was renamed or dropped and this object's DDL was never
/// updated to match. Not raised for a reference that's merely ambiguous (multiple same-named objects in
/// scope) - that's a different, non-"missing" situation - nor for one that lands outside what was
/// extracted at all (a link nothing in the catalog answers to, or a database nobody extracts): that
/// target isn't dangling, it's just not here, and it's recorded as a
/// <see cref="CatalogLinkedServerReference"/> instead when a link was involved.
/// </summary>
public sealed record CatalogOrphanedReference
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>The linked server / database link the reference named, when it named one.</summary>
    [JsonPropertyName("server")]
    public string? Server { get; init; }

    /// <summary>The database the reference named, when it named one.</summary>
    [JsonPropertyName("database")]
    public string? Database { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// One reference that crosses a linked server / database link: which object made it, which link it
/// crossed, the target as the DDL wrote it, and the target node when the catalog has it extracted (null
/// when the hop leaves the catalog's scope). Collected so a link object can list everything reached
/// through it, including the parts of the fleet nobody extracts.
/// </summary>
public sealed record CatalogLinkedServerReference
{
    /// <summary>Node id of the LinkedServers/DatabaseLinks object the reference crosses.</summary>
    [JsonPropertyName("linkedServer")]
    public required string LinkedServer { get; init; }

    /// <summary>Node id of the object whose DDL makes the reference.</summary>
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>Node id of the referenced object, or null when it isn't in the catalog.</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("database")]
    public string? Database { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>The full catalog.json document consumed by site/.</summary>
public sealed record Catalog
{
    [JsonPropertyName("generatedAt")]
    public required DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("servers")]
    public required IReadOnlyList<string> Servers { get; init; }

    [JsonPropertyName("typeCounts")]
    public required IReadOnlyDictionary<string, int> TypeCounts { get; init; }

    [JsonPropertyName("nodes")]
    public required IReadOnlyList<CatalogNode> Nodes { get; init; }

    [JsonPropertyName("edges")]
    public required IReadOnlyList<CatalogEdge> Edges { get; init; }

    [JsonPropertyName("recentChanges")]
    public IReadOnlyList<CatalogCommit> RecentChanges { get; init; } = [];

    [JsonPropertyName("coChangePairs")]
    public IReadOnlyList<CoChangePair> CoChangePairs { get; init; } = [];

    [JsonPropertyName("orphanedReferences")]
    public IReadOnlyList<CatalogOrphanedReference> OrphanedReferences { get; init; } = [];

    [JsonPropertyName("linkedServerReferences")]
    public IReadOnlyList<CatalogLinkedServerReference> LinkedServerReferences { get; init; } = [];
}
