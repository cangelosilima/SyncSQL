using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Lineage.MsSql.Linting.Rules;

namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>
/// Lints a single T-SQL script: real parse errors (via ScriptDom) plus a small set of style/best-practice
/// rules run over the resulting fragment tree. Parsing is best-effort like <see cref="MsSqlLineageAnalyzer"/>
/// - ScriptDom usually returns a partial AST alongside any parse errors, so rules still run over whatever
/// did parse. "Usually": handed something far enough from T-SQL (a PL/SQL package body, say) it returns no
/// fragment at all, and then the parse errors are the whole result.
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

        // Not annotated as nullable by ScriptDom, but it does return null when nothing parsed at all.
        // Reporting the syntax errors is the right answer there; visiting a null tree is an
        // unhandled NullReferenceException out of `syncsql lint`.
        if (fragment is not null)
        {
            foreach (ITSqlLintRule rule in _rules)
            {
                findings.AddRange(rule.Check(fragment));
            }
        }

        return findings
            .OrderBy(f => f.Line)
            .ThenBy(f => f.Column)
            .ToList();
    }
}
