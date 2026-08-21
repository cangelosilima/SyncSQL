#nullable enable
using Antlr4.Runtime;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle;

/// <summary>
/// Walks a parsed PL/SQL fragment tree collecting lineage signal. Unlike ScriptDom's visitor (where
/// overriding Visit(T) leaves the base class's separate ExplicitVisit(T) recursion untouched), ANTLR's
/// generated PlSqlParserBaseVisitor&lt;TResult&gt; has every VisitXxx default straight to
/// VisitChildren(context) - so every override here that still wants its subtree walked must explicitly
/// call VisitChildren(context) itself, or traversal silently stops there.
/// </summary>
internal sealed class PlSqlLineageVisitor : PlSqlParserBaseVisitor<object?>
{
    public List<ObjectRef> ObjectRefs { get; } = [];
    public Dictionary<string, ObjectRef> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ColumnRef> ColumnRefs { get; } = [];

    /// <summary>
    /// The text of an identifier/id_expression subtree, with Oracle's "delimited identifier" quoting
    /// stripped (a quoted identifier is case-sensitive and may contain characters GetText() would
    /// otherwise return still wrapped in literal double quotes).
    /// </summary>
    private static string GetIdentifierText(RuleContext? context)
    {
        if (context is null)
        {
            return string.Empty;
        }

        string text = context.GetText();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return text;
    }

    private static ObjectRef? FromTableviewName(PlSqlParser.Tableview_nameContext? context)
    {
        if (context?.identifier() is null)
        {
            return null;
        }

        string first = GetIdentifierText(context.identifier());
        // tableview_name: identifier ('.' id_expression)? - qualified when id_expression is present.
        return context.id_expression() is { } idExpression
            ? new ObjectRef(first, GetIdentifierText(idExpression))
            : new ObjectRef(null, first);
    }

    // FROM-clause table/view reference, with its alias when one is given (confirmed against the real
    // parse tree: "FROM app.orders o" nests as table_ref_aux(table_ref_aux_internal(
    // dml_table_expression_clause(tableview_name)), table_alias) - alias and target are both reachable
    // from this node, so this is where alias binding actually happens. VisitTableview_name below is the
    // broader, alias-less safety net for every other place a table name appears (DML targets, FK
    // REFERENCES, package-qualified calls elsewhere).
    public override object? VisitTable_ref_aux(PlSqlParser.Table_ref_auxContext context)
    {
        ObjectRef? objRef = context.table_ref_aux_internal() is PlSqlParser.Table_ref_aux_internal_oneContext internalOne
            ? FromTableviewName(internalOne.dml_table_expression_clause()?.tableview_name())
            : null;

        if (objRef is not null)
        {
            ObjectRefs.Add(objRef);

            string? alias = context.table_alias()?.identifier() is { } aliasIdentifier
                ? GetIdentifierText(aliasIdentifier)
                : null;
            if (!string.IsNullOrEmpty(alias))
            {
                Aliases[alias] = objRef;
            }
            Aliases.TryAdd(objRef.Name, objRef);
        }

        return VisitChildren(context);
    }

    private static ObjectRef? FromRoutineName(PlSqlParser.Routine_nameContext? context)
    {
        if (context?.identifier() is null)
        {
            return null;
        }

        string first = GetIdentifierText(context.identifier());
        PlSqlParser.Id_expressionContext[] rest = context.id_expression();
        if (rest.Length == 0)
        {
            return new ObjectRef(null, first);
        }

        string? schema = rest.Length >= 2 ? GetIdentifierText(rest[^2]) : first;
        return new ObjectRef(schema, GetIdentifierText(rest[^1]));
    }

    // Standalone procedure-call statements (app.other_proc();) use a dedicated call_statement/
    // routine_name rule, not general_element - confirmed against the real parse tree.
    public override object? VisitRoutine_name(PlSqlParser.Routine_nameContext context)
    {
        ObjectRef? objRef = FromRoutineName(context);
        if (objRef is not null)
        {
            ObjectRefs.Add(objRef);
        }

        return VisitChildren(context);
    }

    // Broad safety net: every other place a table/view/package name appears as a plain
    // "[schema.]name" - FK REFERENCES targets, DML statement targets (UPDATE/DELETE/INSERT INTO),
    // %TYPE/%ROWTYPE anchors, synonym/package-qualified references. Deliberately not alias-aware (there
    // usually isn't one at these sites); duplicates of what VisitGeneral_table_ref already added are
    // harmless; edge dedup happens downstream in the catalog builder.
    public override object? VisitTableview_name(PlSqlParser.Tableview_nameContext context)
    {
        ObjectRef? objRef = FromTableviewName(context);
        if (objRef is not null)
        {
            ObjectRefs.Add(objRef);
        }

        return VisitChildren(context);
    }

    // "a.b" / "a.b.c(...)" dotted-identifier chains - PL/SQL's grammar doesn't distinguish
    // "alias.column" from "package.procedure" at the syntax level (that needs semantic/catalog
    // knowledge this analyzer doesn't have), so this applies the same best-effort heuristic as
    // everywhere else in this project: a trailing function_argument means the last part is a call
    // target (schema/package qualifier + name -> ObjectRef); otherwise the last two parts are recorded
    // as an "alias.column" reference, same shape ScriptDom's column binding produces for MSSQL.
    //
    // general_element is left-recursive (general_element: general_element PERIOD general_element_part
    // | general_element_part), confirmed against the real parse tree: "app.my_func" nests as
    // general_element(general_element(general_element_part[app]), PERIOD, general_element_part[my_func])
    // rather than one flat general_element_part() array - so the qualifier for THIS node's own
    // (single, in practice) trailing part is the LAST part of the nested general_element child, not a
    // second entry in this node's own array.
    public override object? VisitGeneral_element(PlSqlParser.General_elementContext context)
    {
        PlSqlParser.General_element_partContext[] parts = context.general_element_part();
        if (parts.Length == 0)
        {
            return VisitChildren(context);
        }

        PlSqlParser.General_element_partContext last = parts[^1];
        string lastName = GetIdentifierText(last.id_expression());
        string? qualifier = parts.Length >= 2
            ? GetIdentifierText(parts[^2].id_expression())
            : GetLastPartName(context.general_element());

        if (last.function_argument().Length > 0)
        {
            if (!string.IsNullOrEmpty(lastName))
            {
                ObjectRefs.Add(new ObjectRef(qualifier, lastName));
            }
        }
        else if (!string.IsNullOrEmpty(qualifier) && !string.IsNullOrEmpty(lastName))
        {
            ColumnRefs.Add(new ColumnRef(qualifier, lastName));
        }

        return VisitChildren(context);
    }

    private static string? GetLastPartName(PlSqlParser.General_elementContext? context)
    {
        if (context is null)
        {
            return null;
        }

        PlSqlParser.General_element_partContext[] parts = context.general_element_part();
        return parts.Length > 0 ? GetIdentifierText(parts[^1].id_expression()) : GetLastPartName(context.general_element());
    }
}
