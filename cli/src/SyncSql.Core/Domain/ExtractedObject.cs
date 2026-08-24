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

/// <summary>Everything one extractor run produced for one server: the objects, plus this run's metrics snapshots keyed by object id.</summary>
public sealed record ExtractionOutcome
{
    public required IReadOnlyList<ExtractedObject> Objects { get; init; }
    public required IReadOnlyDictionary<string, MetricsSnapshot> MetricsSnapshots { get; init; }
}
