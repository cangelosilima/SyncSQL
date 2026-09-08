using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.DdlAssembly;

internal static class TypeDdlBuilder
{
    public static string Build(TypeRow type, IEnumerable<ColumnDefinitionRow> columns)
    {
        string name = $"{SqlText.Identifier(type.SchemaName)}.{SqlText.Identifier(type.TypeName)}";
        string ddl = $"CREATE TYPE {name}";
        if (type.IsTableType)
        {
            List<string> definitions = [.. columns.Select(column => "    " + ColumnDdlBuilder.Build(column, tableType: true))];
            if (!string.IsNullOrWhiteSpace(type.ConstraintsDdl))
            {
                definitions.Add(type.ConstraintsDdl);
            }

            ddl += " AS TABLE (\n" + string.Join(",\n", definitions) + "\n)";
            if (type.IsMemoryOptimized)
            {
                ddl += " WITH (MEMORY_OPTIMIZED = ON)";
            }
        }
        else if (type.IsAssemblyType)
        {
            ddl += $" EXTERNAL NAME {SqlText.Identifier(type.AssemblyName!)}.{SqlText.Identifier(type.AssemblyClass!)}";
        }
        else
        {
            ddl += " FROM " + ColumnDdlBuilder.DataType(type.BaseTypeName, type.MaxLength, type.Precision, type.Scale) +
                (type.IsNullable ? " NULL" : " NOT NULL");
        }
        ddl += ";";
        if (type.DefaultName is not null)
        {
            ddl += $"\nGO\nEXEC sys.sp_bindefault @defname = {SqlText.Literal(type.DefaultName)}, @objname = {SqlText.Literal(name)};";
        }

        if (type.RuleName is not null)
        {
            ddl += $"\nGO\nEXEC sys.sp_bindrule @rulename = {SqlText.Literal(type.RuleName)}, @objname = {SqlText.Literal(name)};";
        }

        return ddl;
    }
}
