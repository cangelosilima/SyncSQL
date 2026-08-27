using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Linting.Rules;

/// <summary>Flags `DECLARE ... CURSOR` - row-by-row processing that is almost always slower than an equivalent set-based rewrite, and easy to forget to CLOSE/DEALLOCATE on an error path.</summary>
internal sealed class CursorUsageRule : TSqlLintRuleVisitor
{
    protected override string RuleId => "cursor-usage";

    protected override TSqlLintSeverity Severity => TSqlLintSeverity.Warning;

    public override void Visit(DeclareCursorStatement node)
        => Flag(node, "Cursors process rows one at a time and are usually much slower than a set-based rewrite - consider whether this can be expressed as a single query.");
}
