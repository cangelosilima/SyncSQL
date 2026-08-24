namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>
/// Only plain rowstore CLUSTERED/NONCLUSTERED indexes get a runnable CREATE INDEX line; anything else
/// (columnstore, XML, spatial, hash) gets an informational comment instead of a guessed-at DDL. A direct
/// port of Get-SyncSqlMsSqlIndexes' foreach body.
/// </summary>
internal static class IndexDdlBuilder
{
    public static string Build(string schema, string table, string indexName, bool isUnique, string typeDesc, string? keyColumns, string? includedColumns)
    {
        if (typeDesc is not ("CLUSTERED" or "NONCLUSTERED"))
        {
            return $"-- Index [{indexName}] ({typeDesc}) - see sys.indexes for full definition";
        }

        string prefix = isUnique ? $"UNIQUE {typeDesc} INDEX" : $"{typeDesc} INDEX";
        string line = $"CREATE {prefix} [{indexName}] ON [{schema}].[{table}] ({keyColumns})";
        if (!string.IsNullOrWhiteSpace(includedColumns))
        {
            line += $" INCLUDE ({includedColumns})";
        }

        return line + ";";
    }
}
