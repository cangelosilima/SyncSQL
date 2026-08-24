namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>
/// MSSQL doesn't store a reusable "CREATE TABLE" text the way it does for procedures/views - this
/// assembles the final statement from the column-list and primary-key fragments MsSqlQueries.Tables'
/// STUFF/FOR XML already computed in SQL. Pure and unit-testable: no DB access, just string in, string
/// out - a direct port of SyncSql.MsSql.psm1's Get-SyncSqlMsSqlTables foreach body.
/// </summary>
internal static class TableDdlBuilder
{
    public static string Build(string schema, string table, string? columnsDdl, string? primaryKeyDdl)
    {
        List<string> lines = [$"CREATE TABLE [{schema}].[{table}] ("];

        if (!string.IsNullOrEmpty(columnsDdl))
        {
            lines.Add(columnsDdl);
        }

        if (!string.IsNullOrWhiteSpace(primaryKeyDdl))
        {
            lines[^1] += ",";
            lines.Add(primaryKeyDdl);
        }

        lines.Add(");");
        return string.Join('\n', lines);
    }
}
