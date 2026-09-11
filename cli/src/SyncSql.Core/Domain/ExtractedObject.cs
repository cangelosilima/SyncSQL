using System.Text.Json.Serialization;
using SyncSql.Core.Json;

namespace SyncSql.Core.Domain;

/// <summary>A column's name/type/description, independent of whether it has a documented description. Also the catalog.json "columns[]" shape.</summary>
public sealed record ExtractedColumn(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dataType")] string? DataType,
    [property: JsonPropertyName("description")] string? Description);

/// <summary>'GRANT' or 'DENY' (MSSQL only supports DENY; Oracle grants are always 'GRANT') - see <see cref="Json.GrantStateJsonConverter"/>.</summary>
[JsonConverter(typeof(GrantStateJsonConverter))]
public enum GrantState
{
    Grant,
    Deny,
}

/// <summary>One object- or column-level GRANT/DENY entry (MSSQL: sys.database_permissions; Oracle: ALL_TAB_PRIVS/ALL_COL_PRIVS). Also the catalog.json "grants[]" shape.</summary>
public sealed record GrantEntry(
    [property: JsonPropertyName("permission")] string Permission,
    [property: JsonPropertyName("state")] GrantState State,
    [property: JsonPropertyName("grantee")] string Grantee,
    [property: JsonPropertyName("granteeType")] string? GranteeType,
    [property: JsonPropertyName("column")] string? Column);

/// <summary>An appended, marker-delimited section (Foreign Keys, Check Constraints, Indexes, Grants, Columns, ...). Also the catalog.json "sections[]" shape.</summary>
public sealed record ExtractedSection(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// One object as produced by a database extraction backend (<see cref="Abstractions.IDatabaseObjectExtractor"/>),
/// before catalog assembly. Mirrors the shape SyncSql.MsSql.psm1/SyncSql.Oracle.psm1 used to write into each
/// extracted .sql file's header + body + appended sections.
/// </summary>
public sealed record ExtractedObject
{
    public required string Server { get; init; }
    public required string Database { get; init; }
    /// <summary>Database routing context for BEGIN DIALOG; not part of the logical object identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ServiceBrokerGuid { get; init; }
    public string? Schema { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string Ddl { get; init; }
    public required DatabaseEngine Engine { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ExtractedColumn> Columns { get; init; } = [];
    public IReadOnlyList<GrantEntry> Grants { get; init; } = [];
    public IReadOnlyList<ExtractedSection> Sections { get; init; } = [];

    public string QualifiedName => Schema is { Length: > 0 } ? $"{Schema}.{Name}" : Name;
}

/// <summary>
/// One linked server (MSSQL) / database link (Oracle) an extraction run saw declared on the server it
/// was extracting, reported back so the caller can decide whether to follow it up and extract the server
/// on the other side too - see <see cref="Configuration.LinkedServerFollowUp"/>. Separate from the
/// LinkedServers objects the same run extracts: those are DDL to diff, this is a lead to chase.
/// </summary>
public sealed record DiscoveredLinkedServer
{
    /// <summary>The link's name, as references write it.</summary>
    public required string Name { get; init; }

    /// <summary>sys.servers.product - "SQL Server" for the links a follow-up extraction can actually handle.</summary>
    public string? Product { get; init; }

    /// <summary>sys.servers.provider - e.g. SQLNCLI/MSOLEDBSQL for a SQL Server target.</summary>
    public string? Provider { get; init; }

    /// <summary>The host (optionally "host,port" or "host\instance") the link points at.</summary>
    public string? DataSource { get; init; }

    /// <summary>The database the link itself pins, when it pins one.</summary>
    public string? Catalog { get; init; }

    /// <summary>The remote logins this link maps to, from sys.linked_logins - what decides whether the credentials in hand will work on the other side.</summary>
    public IReadOnlyList<string> RemoteLoginNames { get; init; } = [];

    /// <summary>Whether any login mapping passes the local login through unchanged (sys.linked_logins.uses_self_credential = 1), i.e. the same username reaches the other side.</summary>
    public bool UsesLocalLogin { get; init; }
}

/// <summary>Everything one extractor run produced for one server: the objects, this run's metrics snapshots keyed by object id, and any linked servers it saw declared.</summary>
public sealed record ExtractionOutcome
{
    public required IReadOnlyList<ExtractedObject> Objects { get; init; }
    public required IReadOnlyDictionary<string, MetricsSnapshot> MetricsSnapshots { get; init; }

    /// <summary>Empty unless <see cref="Abstractions.ExtractionOptions.DiscoverLinkedServers"/> asked for it.</summary>
    public IReadOnlyList<DiscoveredLinkedServer> DiscoveredLinkedServers { get; init; } = [];
}
