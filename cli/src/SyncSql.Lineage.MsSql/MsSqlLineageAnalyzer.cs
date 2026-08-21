using System.Text.RegularExpressions;
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
    private static readonly Regex ParserTypeNamePattern = new(@"^TSql(\d+)Parser$", RegexOptions.Compiled);
    private static TSqlParser? _cachedParser;

    public DatabaseEngine Engine => DatabaseEngine.MsSql;

    public LineageAnalysisResult Analyze(string ddl)
    {
        if (string.IsNullOrWhiteSpace(ddl))
        {
            return LineageAnalysisResult.Empty;
        }

        try
        {
            TSqlParser parser = GetParser();
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

    /// <summary>
    /// Finds the newest TSqlNNNParser type via reflection rather than a hardcoded class name -
    /// Microsoft adds a new one roughly per SQL Server release, and hardcoding one would silently stop
    /// picking up newer syntax support on a ScriptDom upgrade instead of just working. $true = quoted
    /// identifiers on, matching this project's extracted DDL (identifiers are bracket-quoted).
    /// </summary>
    private static TSqlParser GetParser()
    {
        if (_cachedParser is not null)
        {
            return _cachedParser;
        }

        Type parserType = typeof(TSqlFragmentVisitor).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true } && ParserTypeNamePattern.IsMatch(t.Name))
            .OrderByDescending(t => int.Parse(ParserTypeNamePattern.Match(t.Name).Groups[1].Value))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Could not find any TSqlNNNParser type in the loaded ScriptDom assembly.");

        // Activator.CreateInstance(Type, bool) is NOT "invoke the (bool) constructor" - that overload
        // means "use a non-public constructor if needed" and requires a parameterless one to exist.
        // The (Type, object?[]?) overload is the one that actually passes constructor arguments.
        _cachedParser = (TSqlParser)Activator.CreateInstance(parserType, [true])!;
        return _cachedParser;
    }
}
