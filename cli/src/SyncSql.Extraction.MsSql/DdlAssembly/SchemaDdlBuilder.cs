namespace SyncSql.Extraction.MsSql.DdlAssembly;

internal static class SchemaDdlBuilder
{
    public static string Build(string schema, string owner) =>
        $"CREATE SCHEMA {SqlText.Identifier(schema)} AUTHORIZATION {SqlText.Identifier(owner)};";
}
