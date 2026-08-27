using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Real T-SQL-parser-based lineage inference for MSSQL objects (Microsoft.SqlServer.TransactSql.ScriptDom),
/// replacing the regex-over-text approach the original PowerShell pipeline used for every engine
/// uniformly. Doesn't match identifiers inside string literals/comments, doesn't misread SELECT * or
/// computed columns as references, and binds "alias.column" to the alias's real table rather than
/// guessing from nearby text. Still can't see dynamic SQL or cross-linked-server four-part names beyond
/// the immediate reference - same inherent limits as any static analysis.
/// </summary>
public sealed class MsSqlLineageAnalyzer(ILogger<MsSqlLineageAnalyzer> logger) : ILineageAnalyzer
{
    public DatabaseEngine Engine => DatabaseEngine.MsSql;

    public LineageAnalysisResult Analyze(string ddl)
    {
        if (string.IsNullOrWhiteSpace(ddl))
        {
            return LineageAnalysisResult.Empty;
        }

        try
        {
            TSqlParser parser = TSqlParserFactory.GetParser();
            using StringReader reader = new(ddl);
            TSqlFragment fragment = parser.Parse(reader, out IList<ParseError> errors);

            if (errors.Count > 0)
            {
                logger.LogWarning("ScriptDom parse produced {Count} error(s) (continuing with the partial AST): {Message}", errors.Count, errors[0].Message);
            }

            TSqlLineageVisitor visitor = new();
            fragment.Accept(visitor);

            return new LineageAnalysisResult
            {
                ObjectRefs = visitor.ObjectRefs,
                Aliases = visitor.Aliases,
                ColumnRefs = visitor.ColumnRefs,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning("ScriptDom parsing failed (skipping lineage for this object): {Message}", ex.Message);
            return LineageAnalysisResult.Empty;
        }
    }
}
