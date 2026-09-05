using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Real T-SQL-parser-based lineage inference for MSSQL objects (Microsoft.SqlServer.TransactSql.ScriptDom),
/// replacing the regex-over-text approach the original PowerShell pipeline used for every engine
/// uniformly. Doesn't misread SELECT * or computed columns as references, and binds "alias.column" to the
/// alias's real table rather than guessing from nearby text.
///
/// String literals are still never scanned for identifiers *in general* - an object name mentioned in a
/// message or a comment is not a reference and never becomes one. The exception is the handful of places
/// T-SQL genuinely executes a string as SQL (OPENQUERY, EXEC of a string, a variable built up and then
/// executed), which <see cref="TSqlLineageVisitor"/> hands to <see cref="DynamicSqlScanner"/>; whatever
/// comes back is tagged <see cref="ReferenceOrigin.Dynamic"/> so it stays distinguishable downstream.
/// </summary>
public sealed class MsSqlLineageAnalyzer(ILogger<MsSqlLineageAnalyzer> logger) : ILineageAnalyzer
{
    public DatabaseEngine Engine => DatabaseEngine.MsSql;

    public LineageAnalysisResult Analyze(string ddl, LineageAnalysisOptions? options = null)
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

            TSqlLineageVisitor visitor = new((options ?? LineageAnalysisOptions.Default).DynamicSql);
            fragment.Accept(visitor);

            return new LineageAnalysisResult
            {
                ObjectRefs = WithoutLocalNames(visitor),
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

    /// <summary>
    /// Drops the references that are really just names this script introduced for itself. A CTE is read
    /// exactly like a table, so the AST reports it as one; since no catalog object answers to it, every
    /// <c>WITH</c> clause used to produce a permanent "orphaned reference" for what is ordinary, correct
    /// T-SQL. (Temp tables are dropped earlier, in the visitor, because they can be recognized from the
    /// name alone - a CTE cannot, so it needs the full set of declared names, which only exists once the
    /// walk is over.) Only unqualified names are considered: <c>dbo.Orders</c> is a real object even if
    /// some CTE elsewhere in the batch happens to be called <c>Orders</c>.
    /// </summary>
    private static List<ObjectRef> WithoutLocalNames(TSqlLineageVisitor visitor)
    {
        if (visitor.CommonTableExpressionNames.Count == 0)
        {
            return visitor.ObjectRefs;
        }

        return [.. visitor.ObjectRefs.Where(reference =>
            !string.IsNullOrWhiteSpace(reference.Schema) || !visitor.CommonTableExpressionNames.Contains(reference.Name))];
    }
}
