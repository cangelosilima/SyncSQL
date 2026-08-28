using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Linting.Rules;

/// <summary>Flags `SELECT *` (and `SELECT alias.*`) - an added/dropped/reordered column silently changes what callers get back.</summary>
internal sealed class SelectStarRule : TSqlLintRuleVisitor
{
    protected override string RuleId => "select-star";

    protected override TSqlLintSeverity Severity => TSqlLintSeverity.Warning;

    public override void Visit(SelectStarExpression node)
        => Flag(node, "SELECT * returns whatever columns the table currently has, in whatever order - list the columns explicitly so schema changes can't silently break callers.");
}
