using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>One style/best-practice check run over an already-parsed T-SQL fragment tree.</summary>
public interface ITSqlLintRule
{
    public IReadOnlyList<TSqlLintFinding> Check(TSqlFragment fragment);
}
