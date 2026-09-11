#nullable enable
using Antlr4.Runtime;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle;

/// <summary>
/// Real PL/SQL-grammar-based lineage inference for Oracle objects (the vendored antlr/grammars-v4
/// PlSqlLexer/PlSqlParser, compiled from the generated C# checked into grammar/ - see
/// grammar/README.md at the repository root), replacing the comment/string-scrubbing-plus-regex
/// approach the original PowerShell pipeline used as an interim fix. A real parse tree naturally can't
/// mistake string-literal or comment content for identifiers (the lexer simply never tokenizes it as
/// one), including Oracle's q'...' alternative quoting, which the old regex-based approach had no way
/// to recognize at all.
/// </summary>
public sealed class OracleLineageAnalyzer : ILineageAnalyzer
{
    private readonly ILogger<OracleLineageAnalyzer> _logger;
    private readonly Func<PlSqlParser, PlSqlParser.Sql_scriptContext> _parse;

    public OracleLineageAnalyzer(ILogger<OracleLineageAnalyzer> logger)
        : this(logger, parser => parser.sql_script()) { }

    internal OracleLineageAnalyzer(ILogger<OracleLineageAnalyzer> logger, Func<PlSqlParser, PlSqlParser.Sql_scriptContext> parse)
    {
        _logger = logger;
        _parse = parse;
    }

    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    /// <summary>
    /// Literal EXECUTE IMMEDIATE statements are parsed with the Oracle grammar.
    /// Nested scans have a shared count budget and depth/size bounds.
    /// </summary>
    public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null)
        => AnalyzeCore(ddl, options ?? LineageAnalysisOptions.Default, 0, new DynamicBudget());

    private sealed class DynamicBudget
    {
        public int Remaining { get; set; } = 64;
    }

    private LineageAnalysisResult AnalyzeCore(string ddl, LineageAnalysisOptions options, int depth, DynamicBudget budget)
    {
        if (string.IsNullOrWhiteSpace(ddl))
        {
            return LineageAnalysisResult.Empty;
        }

        try
        {
            AntlrInputStream inputStream = new(ddl);
            PlSqlLexer lexer = new(inputStream);
            CollectingErrorListener errorListener = new();
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errorListener);

            CommonTokenStream tokenStream = new(lexer);
            PlSqlParser parser = new(tokenStream);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);

            PlSqlParser.Sql_scriptContext tree = _parse(parser);

            if (errorListener.Errors.Count > 0)
            {
                _logger.LogWarning("PL/SQL parse produced {Count} error(s) (continuing with the partial tree): {Message}", errorListener.Errors.Count, errorListener.Errors[0]);
            }

            PlSqlLineageVisitor visitor = new(options.DynamicSql ? sql =>
                depth < 4 && sql.Length <= 65536 && budget.Remaining-- > 0
                    ? AnalyzeCore(sql.EndsWith(';') ? sql : sql + ";", options, depth + 1, budget)
                    : LineageAnalysisResult.Empty : null);
            visitor.Visit(tree);

            return new LineageAnalysisResult
            {
                ObjectRefs = visitor.ObjectRefs,
                Aliases = visitor.Aliases,
                ColumnRefs = visitor.ColumnRefs,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("PL/SQL parsing failed (skipping lineage for this object): {Message}", ex.Message);
            return LineageAnalysisResult.Empty;
        }
    }
}
