using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>
/// Base for a rule implemented as a single-purpose <see cref="TSqlFragmentVisitor"/> - same shape as
/// <see cref="TSqlLineageVisitor"/>, one rule per visitor, overriding only the node types it cares about.
/// </summary>
internal abstract class TSqlLintRuleVisitor : TSqlFragmentVisitor, ITSqlLintRule
{
    private readonly List<TSqlLintFinding> _findings = [];

    protected abstract string RuleId { get; }

    protected abstract TSqlLintSeverity Severity { get; }

    protected void Flag(TSqlFragment node, string message)
        => _findings.Add(new TSqlLintFinding(node.StartLine, node.StartColumn, RuleId, Severity, message));

    public IReadOnlyList<TSqlLintFinding> Check(TSqlFragment fragment)
    {
        _findings.Clear();
        fragment.Accept(this);
        return _findings.Count == 0 ? [] : [.. _findings];
    }
}
