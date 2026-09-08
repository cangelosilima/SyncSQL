using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.DdlAssembly;

internal static class ModuleDdlBuilder
{
    public static string Build(ModuleObjectRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Definition))
        {
            return "-- Definition unavailable: the module is encrypted or VIEW DEFINITION permission is missing.";
        }
        string ddl = $"SET ANSI_NULLS {(row.UsesAnsiNulls ? "ON" : "OFF")};\nGO\n" +
            $"SET QUOTED_IDENTIFIER {(row.UsesQuotedIdentifier ? "ON" : "OFF")};\nGO\n{row.Definition}";
        if (row.TypeCode.Trim() == "TR" && row.IsDisabled)
        {
            ddl += $"\nGO\nDISABLE TRIGGER {SqlText.Identifier(row.SchemaName)}.{SqlText.Identifier(row.ObjectName)} ON " +
                $"{SqlText.Identifier(row.ParentSchemaName!)}.{SqlText.Identifier(row.ParentObjectName!)};";
        }
        return ddl;
    }
}
