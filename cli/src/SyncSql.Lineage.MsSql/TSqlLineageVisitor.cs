using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Walks a parsed T-SQL fragment tree collecting lineage signal: table/view references, FK REFERENCES
/// targets, schema-qualified function calls, EXEC targets, and column references bound to their actual
/// FROM-clause alias. Overriding Visit(T) rather than ExplicitVisit(T) is deliberate: the base
/// ExplicitVisit(T) default already calls Visit(T) then AcceptChildren(this), so overriding only Visit
/// gets automatic recursion into children for free.
/// </summary>
internal sealed class TSqlLineageVisitor : TSqlFragmentVisitor
{
    public List<ObjectRef> ObjectRefs { get; } = [];
    public Dictionary<string, ObjectRef> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ColumnRef> ColumnRefs { get; } = [];

    // ScriptDom hands back the parts of a 1- to 4-part name individually, so a cross-database
    // ("OtherDb.dbo.Orders") or cross-linked-server ("LNK.OtherDb.dbo.Orders") reference keeps the
    // qualifiers it was written with instead of collapsing to "dbo.Orders" - the resolver needs them to
    // look outside this object's own database (see SyncSql.Catalog's NodeIndex). "Srv..dbo.Orders" is
    // legal T-SQL and leaves the database part empty; Empty(...) normalizes that back to null so it
    // means the same thing as "not written".
    private static ObjectRef? FromSchemaObjectName(SchemaObjectName? name)
    {
        if (name?.BaseIdentifier is null)
        {
            return null;
        }

        return new ObjectRef(Empty(name.SchemaIdentifier?.Value), name.BaseIdentifier.Value)
        {
            Database = Empty(name.DatabaseIdentifier?.Value),
            Server = Empty(name.ServerIdentifier?.Value),
        };
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // FROM/JOIN/INTO/UPDATE/DELETE targets - anything ScriptDom represents as a plain named table/view
    // reference.
    public override void Visit(NamedTableReference node)
    {
        ObjectRef? objRef = FromSchemaObjectName(node.SchemaObject);
        if (objRef is null)
        {
            return;
        }

        ObjectRefs.Add(objRef);

        if (node.Alias?.Value is { } aliasValue)
        {
            Aliases[aliasValue] = objRef;
        }

        // Also index by the object's own (unaliased) name/base identifier, so "dbo.Orders.OrderId" or a
        // bare "Orders.OrderId" column reference still resolves without requiring an explicit alias.
        Aliases.TryAdd(objRef.Name, objRef);
    }

    // ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY ... REFERENCES other_table (...) - the appended,
    // structurally-extracted Foreign Keys section is real T-SQL, scanned alongside the object's own
    // DDL. REFERENCES targets are a SchemaObjectName directly on the constraint definition, not a
    // NamedTableReference, so this needs its own override.
    public override void Visit(ForeignKeyConstraintDefinition node)
    {
        ObjectRef? objRef = FromSchemaObjectName(node.ReferenceTableName);
        if (objRef is not null)
        {
            ObjectRefs.Add(objRef);
        }
    }

    // Schema-qualified scalar/table-valued function calls (dbo.MyFunc(...)); unqualified calls
    // (MyFunc(...)) are indistinguishable from built-in function calls at the AST level without a full
    // catalog, so those are intentionally left alone - same "don't guess" posture the bare-name
    // resolver already has.
    public override void Visit(FunctionCall node)
    {
        // CallTarget holds only the qualifying prefix (e.g. "dbo" in dbo.MyFunc(...)) - the function's
        // own name is the separate FunctionName property, NOT the last element of
        // CallTarget.MultiPartIdentifier.Identifiers. (A naive "last identifier = name" reading - the
        // same shape SchemaObjectName uses - silently produces the wrong ObjectRef here, since
        // FunctionCall's CallTarget model differs from NamedTableReference's SchemaObject model; this
        // was caught by testing against the real assembly, not a hypothetical.)
        if (node.FunctionName?.Value is null)
        {
            return;
        }
        if (node.CallTarget is not MultiPartIdentifierCallTarget { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers })
        {
            return;
        }

        // The qualifying prefix is read right-to-left - "dbo", "OtherDb.dbo" or "LNK.OtherDb.dbo" -
        // exactly like the schema/database/server parts of a SchemaObjectName, just spelled out as a
        // flat identifier list here.
        ObjectRefs.Add(new ObjectRef(Empty(identifiers[^1].Value), node.FunctionName.Value)
        {
            Database = identifiers.Count >= 2 ? Empty(identifiers[^2].Value) : null,
            Server = identifiers.Count >= 3 ? Empty(identifiers[^3].Value) : null,
        });
    }

    // EXEC/EXECUTE dbo.MyProc ...
    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference
            {
                ProcedureReference.ProcedureReference.Name: { } procedureName,
            })
        {
            return;
        }

        ObjectRef? objRef = FromSchemaObjectName(procedureName);
        if (objRef is not null)
        {
            ObjectRefs.Add(objRef);
        }
    }

    // alias.column / table.column references - only multi-part ones are useful for column-level
    // tagging (a bare column name can't be attributed to a specific source without full binder-level
    // type resolution, which ScriptDom deliberately doesn't do).
    public override void Visit(ColumnReferenceExpression node)
    {
        if (node.MultiPartIdentifier is not { Identifiers.Count: >= 2 } multiPart)
        {
            return;
        }

        IList<Identifier> identifiers = multiPart.Identifiers;
        ColumnRefs.Add(new ColumnRef(identifiers[^2].Value, identifiers[^1].Value));
    }
}
