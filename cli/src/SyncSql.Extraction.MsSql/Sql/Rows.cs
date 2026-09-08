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
    public bool UsesAnsiNulls { get; set; }
    public bool UsesQuotedIdentifier { get; set; }
    public bool IsDisabled { get; set; }
    public string? ParentSchemaName { get; set; }
    public string? ParentObjectName { get; set; }
}

internal sealed class ConfigurationRow
{
    public string Scope { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

internal sealed class TableRow
{
    public int ObjectId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? PrimaryKeyDdl { get; set; }
}

internal sealed class TypeRow
{
    public string SchemaName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string BaseTypeName { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsTableType { get; set; }
    public bool IsAssemblyType { get; set; }
    public bool IsMemoryOptimized { get; set; }
    public int? TableObjectId { get; set; }
    public string? AssemblyName { get; set; }
    public string? AssemblyClass { get; set; }
    public string? ConstraintsDdl { get; set; }
    public string? DefaultName { get; set; }
    public string? RuleName { get; set; }
}

internal sealed class ColumnDefinitionRow
{
    public int ObjectId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string TypeSchema { get; set; } = string.Empty;
    public bool IsUserDefined { get; set; }
    public int MaxLength { get; set; }
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public bool IsNullable { get; set; }
    public string? ComputedDefinition { get; set; }
    public bool IsPersisted { get; set; }
    public string? CollationName { get; set; }
    public string? DefaultName { get; set; }
    public string? DefaultDefinition { get; set; }
    public string? IdentitySeed { get; set; }
    public string? IdentityIncrement { get; set; }
    public bool IdentityNotForReplication { get; set; }
    public bool IsRowGuid { get; set; }
    public bool IsSparse { get; set; }
    public bool IsColumnSet { get; set; }
    public bool IsFileStream { get; set; }
    public string? XmlCollectionSchema { get; set; }
    public string? XmlCollectionName { get; set; }
    public bool IsXmlDocument { get; set; }
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
    public string? FilterDefinition { get; set; }
    public byte FillFactor { get; set; }
    public bool IsPadded { get; set; }
    public bool IgnoreDupKey { get; set; }
    public bool AllowRowLocks { get; set; }
    public bool AllowPageLocks { get; set; }
    public bool IsDisabled { get; set; }
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
    public string? LocalLoginName { get; set; }
    public string? Location { get; set; }
    public bool DataAccess { get; set; }
    public bool Rpc { get; set; }
    public bool RpcOut { get; set; }
    public bool CollationCompatible { get; set; }
    public bool UseRemoteCollation { get; set; }
    public bool LazySchemaValidation { get; set; }
    public bool RemoteProcTransactionPromotion { get; set; }
    public string? CollationName { get; set; }
    public int ConnectTimeout { get; set; }
    public int QueryTimeout { get; set; }
    public string LinkedServerName { get; set; } = string.Empty;
    public string? Product { get; set; }
    public string? Provider { get; set; }
    public string? DataSource { get; set; }
    public string? ProviderString { get; set; }
    public string? Catalog { get; set; }
    public string? RemoteLoginName { get; set; }
    public bool? UsesSelfCredential { get; set; }
}
