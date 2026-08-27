using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Lineage.MsSql.Linting.Rules;

namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>
/// Lints a single T-SQL script: real parse errors (via ScriptDom) plus a small set of style/best-practice
/// rules run over the resulting fragment tree. Parsing is best-effort like <see cref="MsSqlLineageAnalyzer"/>
/// - ScriptDom returns a partial AST alongside any parse errors, so rules still run over whatever did parse.
/// </summary>
public sealed class TSqlLinter
{
    private static readonly IReadOnlyList<ITSqlLintRule> DefaultRules =
    [
        new SelectStarRule(),
        new NoLockHintRule(),
        new CursorUsageRule(),
    ];

    private readonly IReadOnlyList<ITSqlLintRule> _rules;

    public TSqlLinter()
        : this(DefaultRules)
    {
    }

    public TSqlLinter(IReadOnlyList<ITSqlLintRule> rules)
    {
        _rules = rules;
    }

    public IReadOnlyList<TSqlLintFinding> Lint(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        TSqlParser parser = TSqlParserFactory.GetParser();
        using StringReader reader = new(sql);
        TSqlFragment fragment = parser.Parse(reader, out IList<ParseError> errors);

        List<TSqlLintFinding> findings = errors
            .Select(error => new TSqlLintFinding(error.Line, error.Column, "syntax-error", TSqlLintSeverity.Error, error.Message))
            .ToList();

        foreach (ITSqlLintRule rule in _rules)
        {
            findings.AddRange(rule.Check(fragment));
        }

        return findings
            .OrderBy(f => f.Line)
            .ThenBy(f => f.Column)
            .ToList();
    }
}
