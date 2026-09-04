using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Extraction.MsSql.Sql;
using SyncSql.Lineage.MsSql;

namespace SyncSql.Extraction.MsSql.Tests;

/// <summary>
/// The extraction queries are string constants sent straight to a server, so a syntax error in one
/// only surfaces mid-sync, against whichever database happened to reach that query - which is exactly
/// how "AS RowCount" (ROWCOUNT is a reserved word) shipped and cost every table's volume metrics.
/// Parsing them here with the real T-SQL parser turns that into a test failure instead.
/// </summary>
public class MsSqlQueriesTests
{
    private static IEnumerable<(string Name, string Sql)> AllQueries() =>
        typeof(MsSqlQueries)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void EveryQuery_IsSyntacticallyValidTSql()
    {
        TSqlParser parser = TSqlParserFactory.GetParser();
        List<string> failures = [];

        foreach ((string name, string sql) in AllQueries())
        {
            using StringReader reader = new(sql);
            parser.Parse(reader, out IList<ParseError> errors);
            if (errors.Count > 0)
            {
                failures.Add($"{name}: {string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}"))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryQuery_IsFound()
    {
        // Guards the reflection above: a rename that empties the set would make the parse test vacuous.
        Assert.NotEmpty(AllQueries());
    }

    [Fact]
    public void TableVolume_BracketsTheReservedRowCountAlias()
    {
        Assert.Contains("AS [RowCount]", MsSqlQueries.TableVolume, StringComparison.Ordinal);
    }
}
