namespace SyncSql.Extraction.MsSql.Sql;

// Dapper maps these by property name (case-insensitive) against each query's column aliases in
// MsSqlQueries - one row type per query, kept as plain mutable-by-Dapper classes (Dapper materializes
// via property setters, not constructors, for anonymous/simple POCOs).

internal sealed class SchemaRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
}

internal sealed class ModuleObjectRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string TypeCode { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

internal sealed class TableRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? ColumnsDdl { get; set; }
    public string? PrimaryKeyDdl { get; set; }
}

internal sealed class SynonymRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string SynonymName { get; set; } = string.Empty;
    public string BaseObjectName { get; set; } = string.Empty;
}

internal sealed class ExtendedPropertyRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string? PropertyValue { get; set; }
}

internal sealed class GrantRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string GranteeName { get; set; } = string.Empty;
    public string GranteeType { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string StateDesc { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
}

internal sealed class ColumnListRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int OrdinalPosition { get; set; }
}

internal sealed class TableVolumeRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long ReservedKB { get; set; }
    public long DataKB { get; set; }
    public long IndexKB { get; set; }
}

internal sealed class IndexMetricRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public double? FragmentationPct { get; set; }
    public long? PageCount { get; set; }
    public long Seeks { get; set; }
    public long Scans { get; set; }
    public long Lookups { get; set; }
    public long Updates { get; set; }
}

internal sealed class OptimizerStatisticRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string StatName { get; set; } = string.Empty;
    public long? Rows { get; set; }
    public long? RowsSampled { get; set; }
    public int? Steps { get; set; }
    public long? ModificationCounter { get; set; }
    public DateTime? LastUpdated { get; set; }
}

internal sealed class TableSectionRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

internal sealed class IndexRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public string TypeDesc { get; set; } = string.Empty;
    public string? KeyColumns { get; set; }
    public string? IncludedColumns { get; set; }
}

internal sealed class ReplicationRow
{
    public string? PublicationName { get; set; }
    public string? Description { get; set; }
    public string? Articles { get; set; }
}

internal sealed class LinkedServerRow
{
    public string LinkedServerName { get; set; } = string.Empty;
    public string? Product { get; set; }
    public string? Provider { get; set; }
    public string? DataSource { get; set; }
    public string? ProviderString { get; set; }
    public string? Catalog { get; set; }
    public string? RemoteLoginName { get; set; }
    public bool? UsesSelfCredential { get; set; }
}
