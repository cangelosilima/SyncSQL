namespace SyncSql.Extraction.MsSql.DdlAssembly;

internal static class SqlText
{
    public static string Identifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    public static string Literal(string? value) => value is null ? "NULL" : $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
