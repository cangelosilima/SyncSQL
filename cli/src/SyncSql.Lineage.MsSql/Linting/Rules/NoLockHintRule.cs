using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Linting.Rules;

/// <summary>Flags `WITH (NOLOCK)`/`READUNCOMMITTED` table hints - allows dirty, non-repeatable, or phantom reads; usually copy-pasted as a perf fix rather than a deliberate isolation-level choice.</summary>
internal sealed class NoLockHintRule : TSqlLintRuleVisitor
{
    protected override string RuleId => "nolock-hint";

    protected override TSqlLintSeverity Severity => TSqlLintSeverity.Warning;

    public override void Visit(TableHint node)
    {
        if (node.HintKind is TableHintKind.NoLock or TableHintKind.ReadUncommitted)
        {
            Flag(node, "NOLOCK/READUNCOMMITTED allows dirty reads (uncommitted, possibly rolled-back data) - confirm this is an intentional isolation-level trade-off, not a habitual perf fix.");
        }
    }
}
