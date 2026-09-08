namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>
/// Only plain rowstore CLUSTERED/NONCLUSTERED indexes get a runnable CREATE INDEX line; anything else
/// (columnstore, XML, spatial, hash) gets an informational comment instead of a guessed-at DDL. A direct
/// port of Get-SyncSqlMsSqlIndexes' foreach body.
/// </summary>
internal static class IndexDdlBuilder
{
    public static string Build(Sql.IndexRow index)
    {
        string ddl = Build(index.SchemaName, index.TableName, index.IndexName, index.IsUnique, index.TypeDesc, index.KeyColumns, index.IncludedColumns);
        if (index.TypeDesc is not ("CLUSTERED" or "NONCLUSTERED"))
        {
            return ddl;
        }

        ddl = ddl[..^1];
        if (!string.IsNullOrWhiteSpace(index.FilterDefinition))
        {
            ddl += " WHERE " + index.FilterDefinition;
        }

        ddl += FormattableString.Invariant($" WITH (FILLFACTOR = {index.FillFactor}, PAD_INDEX = {Flag(index.IsPadded)}, IGNORE_DUP_KEY = {Flag(index.IgnoreDupKey)}, ALLOW_ROW_LOCKS = {Flag(index.AllowRowLocks)}, ALLOW_PAGE_LOCKS = {Flag(index.AllowPageLocks)});");
        if (index.IsDisabled)
        {
            ddl += $"\nALTER INDEX {SqlText.Identifier(index.IndexName)} ON {SqlText.Identifier(index.SchemaName)}.{SqlText.Identifier(index.TableName)} DISABLE;";
        }

        return ddl;
    }

    private static string Flag(bool value) => value ? "ON" : "OFF";

    public static string Build(string schema, string table, string indexName, bool isUnique, string typeDesc, string? keyColumns, string? includedColumns)
    {
        if (typeDesc is not ("CLUSTERED" or "NONCLUSTERED"))
        {
            return $"-- Index [{indexName}] ({typeDesc}) - see sys.indexes for full definition";
        }

        string prefix = isUnique ? $"UNIQUE {typeDesc} INDEX" : $"{typeDesc} INDEX";
        string line = $"CREATE {prefix} {SqlText.Identifier(indexName)} ON {SqlText.Identifier(schema)}.{SqlText.Identifier(table)} ({keyColumns})";
        if (!string.IsNullOrWhiteSpace(includedColumns))
        {
            line += $" INCLUDE ({includedColumns})";
        }

        return line + ";";
    }
}
