using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql;

/// <summary>
/// Shared entry point for getting a T-SQL parser instance - used by both <see cref="MsSqlLineageAnalyzer"/>
/// and the T-SQL linter, so there is exactly one place that resolves and caches the newest available
/// ScriptDom grammar version.
/// </summary>
public static class TSqlParserFactory
{
    private static readonly Regex ParserTypeNamePattern = new(@"^TSql(\d+)Parser$", RegexOptions.Compiled);
    private static TSqlParser? _cachedParser;

    /// <summary>
    /// Finds the newest TSqlNNNParser type via reflection rather than a hardcoded class name -
    /// Microsoft adds a new one roughly per SQL Server release, and hardcoding one would silently stop
    /// picking up newer syntax support on a ScriptDom upgrade instead of just working. $true = quoted
    /// identifiers on, matching this project's extracted DDL (identifiers are bracket-quoted).
    /// </summary>
    public static TSqlParser GetParser()
    {
        if (_cachedParser is not null)
        {
            return _cachedParser;
        }

        _cachedParser = CreateParser(typeof(TSqlFragmentVisitor).Assembly.GetTypes());
        return _cachedParser;
    }

    internal static TSqlParser CreateParser(IEnumerable<Type> availableTypes)
    {
        Type parserType = availableTypes
            .Where(t => t is { IsPublic: true } && ParserTypeNamePattern.IsMatch(t.Name))
            .OrderByDescending(t => int.Parse(ParserTypeNamePattern.Match(t.Name).Groups[1].Value, CultureInfo.InvariantCulture))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Could not find any TSqlNNNParser type in the loaded ScriptDom assembly.");

        // Activator.CreateInstance(Type, bool) is NOT "invoke the (bool) constructor" - that overload
        // means "use a non-public constructor if needed" and requires a parameterless one to exist.
        // The (Type, object?[]?) overload is the one that actually passes constructor arguments.
        return (TSqlParser)Activator.CreateInstance(parserType, [true])!;
    }
}
