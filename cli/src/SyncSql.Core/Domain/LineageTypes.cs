namespace SyncSql.Core.Domain;

/// <summary>A (possibly schema-qualified) reference to another object, as found by lineage analysis.</summary>
public sealed record ObjectRef(string? Schema, string Name);

/// <summary>An "alias.column" (or "table.column") reference found in a source object's DDL.</summary>
public sealed record ColumnRef(string AliasOrTable, string Column);

/// <summary>
/// The result of analyzing one object's DDL for lineage: every other object it references, the
/// FROM-clause alias bindings used to resolve column references, and the column references
/// themselves. Pure and engine-specific - see <see cref="Abstractions.ILineageAnalyzer"/>.
/// </summary>
public sealed record LineageAnalysisResult
{
    public required IReadOnlyList<ObjectRef> ObjectRefs { get; init; }

    /// <summary>FROM-clause alias (or the referenced object's own unaliased name) -> the object it resolves to.</summary>
    public required IReadOnlyDictionary<string, ObjectRef> Aliases { get; init; }

    public required IReadOnlyList<ColumnRef> ColumnRefs { get; init; }

    public static LineageAnalysisResult Empty { get; } = new()
    {
        ObjectRefs = [],
        Aliases = new Dictionary<string, ObjectRef>(StringComparer.OrdinalIgnoreCase),
        ColumnRefs = [],
    };
}
