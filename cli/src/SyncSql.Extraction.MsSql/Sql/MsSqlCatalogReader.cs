using Dapper;
using Microsoft.Data.SqlClient;

namespace SyncSql.Extraction.MsSql.Sql;

/// <summary>Thin Dapper wrappers, one per MsSqlQueries entry - all the actual SQL text lives in MsSqlQueries; this just runs it and maps rows.</summary>
internal static class MsSqlCatalogReader
{
    public static Task<IEnumerable<string>> GetDatabasesAsync(SqlConnection connection) =>
        connection.QueryAsync<string>(MsSqlQueries.Databases);

    public static Task<IEnumerable<SchemaRow>> GetSchemasAsync(SqlConnection connection) =>
        connection.QueryAsync<SchemaRow>(MsSqlQueries.Schemas);

    public static Task<IEnumerable<ModuleObjectRow>> GetModuleObjectsAsync(SqlConnection connection) =>
        connection.QueryAsync<ModuleObjectRow>(MsSqlQueries.ModuleObjects);

    public static Task<IEnumerable<TableRow>> GetTablesAsync(SqlConnection connection) =>
        connection.QueryAsync<TableRow>(MsSqlQueries.Tables);

    public static Task<IEnumerable<SynonymRow>> GetSynonymsAsync(SqlConnection connection) =>
        connection.QueryAsync<SynonymRow>(MsSqlQueries.Synonyms);

    public static Task<IEnumerable<ExtendedPropertyRow>> GetExtendedPropertiesAsync(SqlConnection connection) =>
        connection.QueryAsync<ExtendedPropertyRow>(MsSqlQueries.ExtendedProperties);

    public static Task<IEnumerable<GrantRow>> GetGrantsAsync(SqlConnection connection) =>
        connection.QueryAsync<GrantRow>(MsSqlQueries.Grants);

    public static Task<IEnumerable<ColumnListRow>> GetColumnListAsync(SqlConnection connection) =>
        connection.QueryAsync<ColumnListRow>(MsSqlQueries.ColumnList);

    public static Task<IEnumerable<TableVolumeRow>> GetTableVolumeAsync(SqlConnection connection) =>
        connection.QueryAsync<TableVolumeRow>(MsSqlQueries.TableVolume);

    public static Task<IEnumerable<IndexMetricRow>> GetIndexMetricsAsync(SqlConnection connection) =>
        connection.QueryAsync<IndexMetricRow>(MsSqlQueries.IndexMetrics);

    public static Task<IEnumerable<OptimizerStatisticRow>> GetOptimizerStatisticsAsync(SqlConnection connection) =>
        connection.QueryAsync<OptimizerStatisticRow>(MsSqlQueries.OptimizerStatistics);

    public static Task<IEnumerable<TableSectionRow>> GetForeignKeysAsync(SqlConnection connection) =>
        connection.QueryAsync<TableSectionRow>(MsSqlQueries.ForeignKeys);

    public static Task<IEnumerable<TableSectionRow>> GetCheckConstraintsAsync(SqlConnection connection) =>
        connection.QueryAsync<TableSectionRow>(MsSqlQueries.CheckConstraints);

    public static Task<IEnumerable<IndexRow>> GetIndexesAsync(SqlConnection connection) =>
        connection.QueryAsync<IndexRow>(MsSqlQueries.Indexes);

    public static Task<IEnumerable<ReplicationRow>> GetReplicationAsync(SqlConnection connection) =>
        connection.QueryAsync<ReplicationRow>(MsSqlQueries.Replication);

    public static Task<IEnumerable<LinkedServerRow>> GetLinkedServersAsync(SqlConnection connection) =>
        connection.QueryAsync<LinkedServerRow>(MsSqlQueries.LinkedServers);
}
