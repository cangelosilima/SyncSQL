using Dapper;
using System.Data.Common;

namespace SyncSql.Extraction.MsSql.Sql;

/// <summary>Thin Dapper wrappers, one per MsSqlQueries entry - all the actual SQL text lives in MsSqlQueries; this just runs it and maps rows.</summary>
internal static class MsSqlCatalogReader
{
    public static Task<Guid?> GetServiceBrokerGuidAsync(DbConnection connection) =>
        connection.QuerySingleOrDefaultAsync<Guid?>(MsSqlQueries.ServiceBrokerGuid);

    public static Task<IEnumerable<ColumnDefinitionRow>> GetColumnDefinitionsAsync(DbConnection connection) =>
        connection.QueryAsync<ColumnDefinitionRow>(MsSqlQueries.ColumnDefinitions);

    public static Task<IEnumerable<TypeRow>> GetTypesAsync(DbConnection connection) =>
        connection.QueryAsync<TypeRow>(MsSqlQueries.Types);

    public static Task<IEnumerable<ConfigurationRow>> GetPermissionsAsync(DbConnection connection) =>
        connection.QueryAsync<ConfigurationRow>(MsSqlQueries.Permissions);

    public static Task<IEnumerable<ConfigurationRow>> GetOwnershipAsync(DbConnection connection) =>
        connection.QueryAsync<ConfigurationRow>(MsSqlQueries.Ownership);

    public static Task<IEnumerable<ConfigurationRow>> GetPropertyDefinitionsAsync(DbConnection connection) =>
        connection.QueryAsync<ConfigurationRow>(MsSqlQueries.PropertyDefinitions);

    public static Task<IEnumerable<string>> GetDatabasesAsync(DbConnection connection) =>
        connection.QueryAsync<string>(MsSqlQueries.Databases);

    public static Task<IEnumerable<SchemaRow>> GetSchemasAsync(DbConnection connection) =>
        connection.QueryAsync<SchemaRow>(MsSqlQueries.Schemas);

    public static Task<IEnumerable<ModuleObjectRow>> GetModuleObjectsAsync(DbConnection connection) =>
        connection.QueryAsync<ModuleObjectRow>(MsSqlQueries.ModuleObjects);

    public static Task<IEnumerable<TableRow>> GetTablesAsync(DbConnection connection) =>
        connection.QueryAsync<TableRow>(MsSqlQueries.Tables);

    public static Task<IEnumerable<SynonymRow>> GetSynonymsAsync(DbConnection connection) =>
        connection.QueryAsync<SynonymRow>(MsSqlQueries.Synonyms);

    public static Task<IEnumerable<ExtendedPropertyRow>> GetExtendedPropertiesAsync(DbConnection connection) =>
        connection.QueryAsync<ExtendedPropertyRow>(MsSqlQueries.ExtendedProperties);

    public static Task<IEnumerable<GrantRow>> GetGrantsAsync(DbConnection connection) =>
        connection.QueryAsync<GrantRow>(MsSqlQueries.Grants);

    public static Task<IEnumerable<ColumnListRow>> GetColumnListAsync(DbConnection connection) =>
        connection.QueryAsync<ColumnListRow>(MsSqlQueries.ColumnList);

    public static Task<IEnumerable<TableVolumeRow>> GetTableVolumeAsync(DbConnection connection) =>
        connection.QueryAsync<TableVolumeRow>(MsSqlQueries.TableVolume);

    public static Task<IEnumerable<IndexMetricRow>> GetIndexMetricsAsync(DbConnection connection) =>
        connection.QueryAsync<IndexMetricRow>(MsSqlQueries.IndexMetrics);

    public static Task<IEnumerable<OptimizerStatisticRow>> GetOptimizerStatisticsAsync(DbConnection connection) =>
        connection.QueryAsync<OptimizerStatisticRow>(MsSqlQueries.OptimizerStatistics);

    public static Task<IEnumerable<TableSectionRow>> GetForeignKeysAsync(DbConnection connection) =>
        connection.QueryAsync<TableSectionRow>(MsSqlQueries.ForeignKeys);

    public static Task<IEnumerable<TableSectionRow>> GetUniqueConstraintsAsync(DbConnection connection) =>
        connection.QueryAsync<TableSectionRow>(MsSqlQueries.UniqueConstraints);

    public static Task<IEnumerable<TableSectionRow>> GetCheckConstraintsAsync(DbConnection connection) =>
        connection.QueryAsync<TableSectionRow>(MsSqlQueries.CheckConstraints);

    public static Task<IEnumerable<IndexRow>> GetIndexesAsync(DbConnection connection) =>
        connection.QueryAsync<IndexRow>(MsSqlQueries.Indexes);

    public static Task<IEnumerable<ReplicationRow>> GetReplicationAsync(DbConnection connection) =>
        connection.QueryAsync<ReplicationRow>(MsSqlQueries.Replication);

    public static Task<IEnumerable<LinkedServerRow>> GetLinkedServersAsync(DbConnection connection) =>
        connection.QueryAsync<LinkedServerRow>(MsSqlQueries.LinkedServers);
}
