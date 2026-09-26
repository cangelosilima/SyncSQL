using System.Text.RegularExpressions;

namespace SyncSql.Extraction.Oracle.Tests;

public sealed class OracleQueriesTests
{
    [Theory]
    [InlineData(OracleQueries.ObjectList)]
    [InlineData(OracleQueries.ApplicationObjectList)]
    [InlineData(OracleQueries.LegacyApplicationObjectList)]
    public void ObjectDiscovery_RequiresStandaloneTableMetadataInEveryMode(string sql)
    {
        Assert.Contains("FROM ALL_OBJECTS o", sql);
        Assert.Contains("o.object_type <> 'TABLE' OR EXISTS", sql);
        Assert.Contains("FROM ALL_ALL_TABLES t", sql);
        Assert.Contains("t.owner = o.owner AND t.table_name = o.object_name", sql);
        Assert.Contains("t.nested = 'NO'", sql);
        Assert.Contains("t.iot_type IS NULL OR t.iot_type = 'IOT'", sql);
        Assert.Contains("o.object_type NOT IN ('TYPE', 'TYPE BODY')", sql);
    }

    [Theory]
    [InlineData("SYS_YOID0000136023$", true)]
    [InlineData("SYS_YOID0000137633$", true)]
    [InlineData("SYS_APPLICATION_TYPE", false)]
    [InlineData("SYS_YOID_REPORT", false)]
    [InlineData("SYS_YOID0000136023", false)]
    [InlineData("SYS_YOID0000136023$CUSTOM", false)]
    [InlineData("sys_yoid0000136023$", false)]
    public void InternalTypePattern_IsLimitedToReportedIdentifierFamily(string name, bool excluded)
    {
        foreach (string sql in new[] { OracleQueries.ObjectList, OracleQueries.ApplicationObjectList, OracleQueries.LegacyApplicationObjectList })
        {
            Match predicate = Regex.Match(sql, "REGEXP_LIKE\\(o.object_name, '([^']+)', 'c'\\)");
            Assert.True(predicate.Success);
            Assert.Equal(excluded, Regex.IsMatch(name, predicate.Groups[1].Value));
        }
    }
}
