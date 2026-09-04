namespace SyncSql.Core.Domain;

/// <summary>
/// A (possibly qualified) reference to another object, as found by lineage analysis. Beyond the schema,
/// a reference can also name the database it lives in (T-SQL's <c>OtherDb.dbo.Orders</c>) and the server
/// it lives on - a linked server in T-SQL (<c>LNK.OtherDb.dbo.Orders</c>) or a database link in PL/SQL
/// (<c>app.orders@LNK</c>). Both are the name as written in the DDL, not a resolved host: mapping a
/// linked-server/DB-link name onto a catalog server is the resolver's job, not the parser's.
/// </summary>
public sealed record ObjectRef(string? Schema, string Name)
{
    /// <summary>The database part of a 3-/4-part T-SQL name, when the DDL spelled one out. Null for an unqualified reference.</summary>
    public string? Database { get; init; }

    /// <summary>The linked-server (T-SQL) or database-link (PL/SQL) name the reference crosses, as written. Null for a same-server reference.</summary>
    public string? Server { get; init; }
}

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
