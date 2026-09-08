using System.Globalization;
using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.DdlAssembly;

internal static class ColumnDdlBuilder
{
    public static string DataType(string name, int length, byte precision, byte scale)
    {
        string size = name.ToLowerInvariant() switch
        {
            "varchar" or "char" or "varbinary" or "binary" => length == -1 ? "MAX" : length.ToString(CultureInfo.InvariantCulture),
            "nvarchar" or "nchar" => length == -1 ? "MAX" : (length / 2).ToString(CultureInfo.InvariantCulture),
            "decimal" or "numeric" => FormattableString.Invariant($"{precision},{scale}"),
            "datetime2" or "datetimeoffset" or "time" => scale.ToString(CultureInfo.InvariantCulture),
            "float" => precision.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
        return name.ToUpperInvariant() + (size.Length > 0 ? $"({size})" : string.Empty);
    }

    public static string Build(ColumnDefinitionRow column, bool tableType = false)
    {
        string ddl = SqlText.Identifier(column.ColumnName);
        if (column.ComputedDefinition is not null)
        {
            return ddl + " AS " + column.ComputedDefinition + (column.IsPersisted ? " PERSISTED" + (!column.IsNullable ? " NOT NULL" : "") : "");
        }
        ddl += " " + (column.IsUserDefined
            ? $"{SqlText.Identifier(column.TypeSchema)}.{SqlText.Identifier(column.TypeName)}"
            : DataType(column.TypeName, column.MaxLength, column.Precision, column.Scale));
        if (column.XmlCollectionName is not null)
        {
            ddl += $"({(column.IsXmlDocument ? "DOCUMENT" : "CONTENT")} {SqlText.Identifier(column.XmlCollectionSchema!)}.{SqlText.Identifier(column.XmlCollectionName)})";
        }
        if (column.IsColumnSet)
        {
            return ddl + " COLUMN_SET FOR ALL_SPARSE_COLUMNS";
        }
        if (column.CollationName is not null && !column.IsUserDefined)
        {
            // COLLATE accepts an engine-defined collation name, not a delimited identifier.
            ddl += " COLLATE " + column.CollationName;
        }
        if (column.IsFileStream)
        {
            ddl += " FILESTREAM";
        }

        if (column.IsSparse)
        {
            ddl += " SPARSE";
        }

        if (column.IdentitySeed is not null)
        {
            ddl += $" IDENTITY({column.IdentitySeed},{column.IdentityIncrement})";
            if (column.IdentityNotForReplication)
            {
                ddl += " NOT FOR REPLICATION";
            }
        }
        if (column.IsRowGuid)
        {
            ddl += " ROWGUIDCOL";
        }

        ddl += column.IsNullable ? " NULL" : " NOT NULL";
        if (column.DefaultDefinition is not null)
        {
            ddl += (!tableType && column.DefaultName is not null ? " CONSTRAINT " + SqlText.Identifier(column.DefaultName) : "") +
                " DEFAULT " + column.DefaultDefinition;
        }
        return ddl;
    }
}
