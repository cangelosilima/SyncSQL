using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Walks a parsed T-SQL fragment tree collecting lineage signal: table/view references, FK REFERENCES
/// targets, schema-qualified function calls, EXEC targets, and column references bound to their actual
/// FROM-clause alias. Overriding Visit(T) rather than ExplicitVisit(T) is deliberate: the base
/// ExplicitVisit(T) default already calls Visit(T) then AcceptChildren(this), so overriding only Visit
/// gets automatic recursion into children for free.
///
/// Where the tree stops - SQL assembled into a string and executed at runtime - the dynamic overrides
/// below hand the text to <see cref="DynamicSqlScanner"/> instead. Those are the only places string
/// content is looked at, and only because they are the places T-SQL actually executes it: an ordinary
/// literal in a SELECT list is still never treated as SQL.
/// </summary>
/// <param name="dynamicSql">Whether to scan dynamically-built SQL at all (see <see cref="Core.Abstractions.LineageAnalysisOptions"/>).</param>
internal sealed class TSqlLineageVisitor(bool dynamicSql = true) : TSqlFragmentVisitor
{
    /// <summary>One budget per object, shared by every nested scan this walk starts.</summary>
    private readonly DynamicSqlScanner.Budget _budget = new();
    private ObjectRef? _triggerTarget;
    public List<ObjectRef> ObjectRefs { get; } = [];
    public Dictionary<string, ObjectRef> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ColumnRef> ColumnRefs { get; } = [];

    /// <summary>
    /// Names a <c>WITH</c> clause introduces for the length of one statement. A CTE is referenced exactly
    /// like a table, so the AST hands it back as an ordinary <see cref="NamedTableReference"/> - and since
    /// no catalog object answers to it, every CTE used to surface as a dangling reference. The analyzer
    /// strips unqualified references matching one of these once the walk is over (a CTE can be declared
    /// after the query that reads it in a recursive form, so filtering mid-walk would be order-dependent).
    /// </summary>
    public HashSet<string> CommonTableExpressionNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The DELETE/UPDATE targets that name a FROM-clause alias rather than a table. T-SQL's multi-table
    /// forms - <c>DELETE a FROM table_a AS a JOIN table_b AS b ON b.id = a.id</c> and the matching
    /// <c>UPDATE a SET ... FROM ...</c> - point the statement at one of the aliases its own FROM clause
    /// declares, and ScriptDom hands that target back as an ordinary <see cref="NamedTableReference"/>
    /// whose name is the alias. Read at face value it becomes a reference to a table called <c>a</c>,
    /// which nothing in the catalog answers to, so ordinary correct T-SQL reported a permanent orphaned
    /// reference. The statement's real target is still collected from the FROM clause, so skipping the
    /// alias loses no lineage.
    ///
    /// Tracked by fragment identity rather than by name: <c>DELETE a FROM a AS a</c> is legal, and there
    /// the FROM clause's own <c>a</c> is a genuine reference that has to survive.
    /// </summary>
    private readonly HashSet<NamedTableReference> _aliasTargets = new(ReferenceEqualityComparer.Instance);

    // ScriptDom hands back the parts of a 1- to 4-part name individually, so a cross-database
    // ("OtherDb.dbo.Orders") or cross-linked-server ("LNK.OtherDb.dbo.Orders") reference keeps the
    // qualifiers it was written with instead of collapsing to "dbo.Orders" - the resolver needs them to
    // look outside this object's own database (see SyncSql.Catalog's NodeIndex). "Srv..dbo.Orders" is
    // legal T-SQL and leaves the database part empty; Empty(...) normalizes that back to null so it
    // means the same thing as "not written".
    private static ObjectRef? FromSchemaObjectName(SchemaObjectName? name)
    {
        if (name?.BaseIdentifier?.Value is not { } baseName)
        {
            return null;
        }

        // "#Staging" / "##Shared" is a temp table: a real object, but one that lives in tempdb for the
        // length of a session and is created by this very script. Nothing extracts it and nothing ever
        // will, so letting it through only produced a permanent "orphaned reference" for what is ordinary,
        // correct T-SQL. Dropping it here rather than at resolution time also keeps it out of Aliases, so
        // it stops contributing phantom entries to column tagging.
        if (baseName.StartsWith('#'))
        {
            return null;
        }

        return new ObjectRef(Empty(name.SchemaIdentifier?.Value), baseName)
        {
            Database = Empty(name.DatabaseIdentifier?.Value),
            Server = Empty(name.ServerIdentifier?.Value),
        };
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // WITH cte AS (...) - remembered so the analyzer can drop the "references" that reading the CTE back
    // produces. Overriding Visit still recurses into the CTE's own body, so real tables inside it are
    // collected as usual.
    public override void Visit(CommonTableExpression node)
    {
        if (node.ExpressionName?.Value is { } name && !string.IsNullOrWhiteSpace(name))
        {
            CommonTableExpressionNames.Add(name);
        }
    }

    // FROM/JOIN/INTO/UPDATE/DELETE targets - anything ScriptDom represents as a plain named table/view
    // reference.
    public override void Visit(NamedTableReference node)
    {
        // An alias standing in for the statement's target (see _aliasTargets) is not a name to resolve.
        if (_aliasTargets.Contains(node))
        {
            return;
        }

        ObjectRef? objRef = FromSchemaObjectName(node.SchemaObject);
        if (objRef is null)
        {
            return;
        }

        if (_triggerTarget is not null && objRef.Schema is null && objRef.Database is null
            && (objRef.Name.Equals("inserted", StringComparison.OrdinalIgnoreCase)
                || objRef.Name.Equals("deleted", StringComparison.OrdinalIgnoreCase)))
        {
            Aliases[objRef.Name] = _triggerTarget;
            objRef = _triggerTarget;
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

    // DELETE a FROM table_a AS a JOIN table_b AS b ON b.id = a.id - the statement is visited before any
    // of its children, so the FROM clause is available here and the decision never depends on the order
    // ScriptDom happens to walk the target and the FROM clause in.
    public override void Visit(DeleteStatement node) =>
        SkipTargetNamingAnAlias(node.DeleteSpecification?.Target, node.DeleteSpecification?.FromClause);

    // UPDATE a SET a.total = b.total FROM table_a AS a JOIN table_b AS b ON b.id = a.id
    public override void Visit(UpdateStatement node) =>
        SkipTargetNamingAnAlias(node.UpdateSpecification?.Target, node.UpdateSpecification?.FromClause);

    /// <summary>
    /// Marks a DELETE/UPDATE target for skipping when it is one of the aliases the statement's own FROM
    /// clause declares (see <see cref="_aliasTargets"/>).
    /// </summary>
    private void SkipTargetNamingAnAlias(TableReference? target, FromClause? fromClause)
    {
        // Only a bare, single-part name can be an alias: "DELETE dbo.table_a FROM dbo.table_a AS a" names
        // the table itself, qualifiers and all, and is a reference like any other.
        if (target is not NamedTableReference named
            || named.SchemaObject is not { } schemaObject
            || Empty(schemaObject.BaseIdentifier?.Value) is not { } targetName
            || Empty(schemaObject.SchemaIdentifier?.Value) is not null
            || Empty(schemaObject.DatabaseIdentifier?.Value) is not null
            || Empty(schemaObject.ServerIdentifier?.Value) is not null
            || fromClause?.TableReferences is not { } tableReferences)
        {
            return;
        }

        HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (TableReference reference in tableReferences)
        {
            CollectAliases(reference, aliases);
        }

        if (aliases.Contains(targetName))
        {
            _aliasTargets.Add(named);
        }
    }

    /// <summary>
    /// Collects the aliases one FROM-clause entry declares, descending through joins so
    /// "FROM table_a AS a JOIN table_b AS b" yields both.
    /// </summary>
    private static void CollectAliases(TableReference? reference, HashSet<string> aliases)
    {
        switch (reference)
        {
            case JoinTableReference join:
                CollectAliases(join.FirstTableReference, aliases);
                CollectAliases(join.SecondTableReference, aliases);
                break;
            case JoinParenthesisTableReference parenthesis:
                CollectAliases(parenthesis.Join, aliases);
                break;
            case TableReferenceWithAlias { Alias.Value: { } alias } when !string.IsNullOrWhiteSpace(alias):
                aliases.Add(alias);
                break;
        }
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
    public override void Visit(TriggerObject node)
    {
        if (FromSchemaObjectName(node.Name) is { } reference)
        {
            _triggerTarget = reference;
            ObjectRefs.Add(reference);
        }
    }

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

    // EXEC/EXECUTE dbo.MyProc ..., and the string forms - EXEC('...'), EXEC sp_executesql N'...',
    // EXEC (@sql) AT LNK - whose body is only SQL at runtime.
    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification is not { } specification)
        {
            return;
        }

        // "AT LNK" says the statement runs on a linked server, so whatever the body reaches lives there.
        string? linkedServer = specification.LinkedServer?.Value;

        switch (specification.ExecutableEntity)
        {
            case ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } procedureName }:
                if (FromSchemaObjectName(procedureName) is { } objRef)
                {
                    ObjectRefs.Add(linkedServer is null ? objRef : objRef with { Server = linkedServer });
                }

                // sp_executesql's own first argument is the statement being run, so it is dynamic SQL in
                // exactly the same way EXEC('...') is - the procedure being called just happens to be the
                // engine's rather than the caller's. Restricted to the procedures that actually execute
                // their argument: a string passed to somebody's dbo.LogMessage is a message, not SQL, and
                // scanning it would invent references out of log text.
                if (ExecutesItsArgument(procedureName))
                {
                    ScanParameters(specification.ExecutableEntity, linkedServer);
                }
                break;

            case ExecutableStringList strings:
                // The pieces of "EXEC ('SELECT ... ' + @where)" are concatenated by the engine before it
                // parses them, so they have to be reassembled before there is anything worth scanning.
                ScanDynamic(BuildLiteralText(strings.Strings), linkedServer);
                break;
        }
    }

    // OPENQUERY(LNK, 'select ...') - the one place a linked server and the remote SQL it runs sit right
    // next to each other, and the reason a reference found in that literal can be attributed to LNK
    // rather than left floating.
    public override void Visit(OpenQueryTableReference node) =>
        ScanDynamic(node.Query?.Value, node.LinkedServer?.Value);

    // OPENROWSET(..., 'select ...') names a provider and connection string rather than a catalogued
    // linked server, so its body is scanned without attributing it anywhere.
    public override void Visit(OpenRowsetTableReference node) => ScanDynamic(node.Query?.Value, null);

    // DECLARE @sql NVARCHAR(MAX) = '...' + @id + '...'
    public override void Visit(DeclareVariableElement node) => ScanDynamic(BuildLiteralText(node.Value), null);

    // SET @sql = '...' + @id + '...'
    public override void Visit(SetVariableStatement node) => ScanDynamic(BuildLiteralText(node.Expression), null);

    /// <summary>The system procedures whose argument is itself a batch to run.</summary>
    private static readonly string[] SqlExecutingProcedures = ["sp_executesql", "sp_execute", "sp_prepexec"];

    /// <summary>True when calling this procedure means "run the string I am passing you".</summary>
    private static bool ExecutesItsArgument(SchemaObjectName name) =>
        name.BaseIdentifier?.Value is { } procedure && SqlExecutingProcedures.Contains(procedure, StringComparer.OrdinalIgnoreCase);

    /// <summary>Scans the arguments of an EXEC, which is where <c>sp_executesql</c> keeps the statement it runs.</summary>
    private void ScanParameters(ExecutableEntity entity, string? linkedServer)
    {
        if (entity.Parameters is null)
        {
            return;
        }

        foreach (ExecuteParameter parameter in entity.Parameters)
        {
            ScanDynamic(BuildLiteralText(parameter.ParameterValue), linkedServer);
        }
    }

    private void ScanDynamic(string? sql, string? linkedServer)
    {
        if (dynamicSql)
        {
            DynamicSqlScanner.Scan(sql, linkedServer, 0, _budget, ObjectRefs);
        }
    }

    /// <summary>
    /// Flattens a string-building expression into the text it would produce, with every runtime value
    /// (a variable, a function call, a column) simply left out. The result is not valid T-SQL and is not
    /// meant to be - <see cref="DynamicSqlScanner"/> lexes rather than parses precisely so that an
    /// incomplete fragment still yields the identifiers it contains.
    /// </summary>
    private static string? BuildLiteralText(ScalarExpression? expression)
    {
        if (expression is null)
        {
            return null;
        }

        StringBuilder builder = new();
        Append(expression, builder, 0);
        return builder.Length == 0 ? null : builder.ToString();

        static void Append(ScalarExpression? node, StringBuilder builder, int depth)
        {
            // A concatenation is left-deep, so depth tracks the number of "+" operands, not nesting of
            // different statements; a few dozen is already an unusually long one.
            if (node is null || depth > 64)
            {
                return;
            }

            switch (node)
            {
                case StringLiteral literal:
                    builder.Append(literal.Value);
                    break;
                case BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add or BinaryExpressionType.Concat } binary:
                    Append(binary.FirstExpression, builder, depth + 1);
                    Append(binary.SecondExpression, builder, depth + 1);
                    break;
                default:
                    break;
            }
        }
    }

    private static string? BuildLiteralText(IList<ValueExpression>? expressions)
    {
        if (expressions is null || expressions.Count == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        foreach (ValueExpression expression in expressions)
        {
            if (BuildLiteralText(expression) is { } text)
            {
                builder.Append(text);
            }
        }
        return builder.Length == 0 ? null : builder.ToString();
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
