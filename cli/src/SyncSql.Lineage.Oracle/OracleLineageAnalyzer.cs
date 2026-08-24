#nullable enable
using Antlr4.Runtime;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle;

/// <summary>
/// Real PL/SQL-grammar-based lineage inference for Oracle objects (the vendored antlr/grammars-v4
/// PlSqlLexer/PlSqlParser - see Grammar/NOTICE.md), replacing the comment/string-scrubbing-plus-regex
/// approach the original PowerShell pipeline used as an interim fix. A real parse tree naturally can't
/// mistake string-literal or comment content for identifiers (the lexer simply never tokenizes it as
/// one), including Oracle's q'...' alternative quoting, which the old regex-based approach had no way
/// to recognize at all.
/// </summary>
public sealed class OracleLineageAnalyzer(ILogger<OracleLineageAnalyzer> logger) : ILineageAnalyzer
{
    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    public LineageAnalysisResult Analyze(string ddl)
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

            PlSqlParser.Sql_scriptContext tree = parser.sql_script();

            if (errorListener.Errors.Count > 0)
            {
                logger.LogWarning("PL/SQL parse produced {Count} error(s) (continuing with the partial tree): {Message}", errorListener.Errors.Count, errorListener.Errors[0]);
            }

            PlSqlLineageVisitor visitor = new();
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
            logger.LogWarning("PL/SQL parsing failed (skipping lineage for this object): {Message}", ex.Message);
            return LineageAnalysisResult.Empty;
        }
    }
}
