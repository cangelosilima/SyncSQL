namespace SyncSql.Core.Domain;

/// <summary>Where in an object's DDL a reference was found, which is what decides how much to trust it.</summary>
public enum ReferenceOrigin
{
    /// <summary>Read straight off the parse tree of the object's own DDL - the parser saw a real table/function/procedure reference.</summary>
    Static,

    /// <summary>
    /// Recovered from SQL built as a string at runtime (a literal, a concatenation assigned to a variable,
    /// an <c>OPENQUERY</c> body). Genuinely useful - a great deal of real T-SQL reaches other objects only
    /// this way - but a weaker signal than a static reference, so it is tagged rather than silently mixed
    /// in, and a dynamic reference that resolves to nothing is never reported as an orphan.
    /// </summary>
    Dynamic,
}

/// <summary>
/// A (possibly qualified) reference to another object, as found by lineage analysis. Beyond the schema,
/// a reference can also name the database it lives in (T-SQL's <c>OtherDb.dbo.Orders</c>) and the server
/// it lives on - a linked server in T-SQL (<c>LNK.OtherDb.dbo.Orders</c>) or a database link in PL/SQL
/// (<c>app.orders@LNK</c>). Both are the name as written in the DDL, not a resolved host: mapping a
/// linked-server/DB-link name onto a catalog server is the resolver's job, not the parser's.
/// </summary>
public sealed record ObjectRef(string? Schema, string Name)
{
    /// <summary>Broker namespace, when syntax identifies a specific database-local object kind.</summary>
    public string? ObjectType { get; init; }

    /// <summary>True for a callable reference, allowing a remote dialect to interpret package-member names.</summary>
    public bool IsRoutine { get; init; }

    /// <summary>The database part of a 3-/4-part T-SQL name, when the DDL spelled one out. Null for an unqualified reference.</summary>
    public string? Database { get; init; }

    /// <summary>The linked-server (T-SQL) or database-link (PL/SQL) name the reference crosses, as written. Null for a same-server reference.</summary>
    public string? Server { get; init; }

    /// <summary>Whether the parse tree of the object's own DDL yielded this, or dynamically-built SQL did. Defaults to <see cref="ReferenceOrigin.Static"/>, so every existing construction site keeps its old meaning.</summary>
    public ReferenceOrigin Origin { get; init; } = ReferenceOrigin.Static;
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
