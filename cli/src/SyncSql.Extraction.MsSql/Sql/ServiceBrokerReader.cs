using Dapper;
using System.Data.Common;

namespace SyncSql.Extraction.MsSql.Sql;

/// <summary>Database-local Broker definitions, excluding engine-provided objects.</summary>
internal static class ServiceBrokerReader
{
    public static readonly string[] ObjectTypes = ["MessageTypes", "Contracts", "Queues", "Services"];

    public static Task<IEnumerable<BrokerRow>> ReadAsync(DbConnection connection) => connection.QueryAsync<BrokerRow>(Query);

    internal const string Query = """
        SELECT 'MessageTypes' AS Type, CAST(NULL AS sysname) AS SchemaName, m.name COLLATE DATABASE_DEFAULT AS Name,
            CAST('CREATE MESSAGE TYPE ' + QUOTENAME(m.name COLLATE DATABASE_DEFAULT) + ' AUTHORIZATION ' + QUOTENAME(USER_NAME(m.principal_id))
            + ' VALIDATION = ' + CASE m.validation WHEN 'N' THEN 'NONE' WHEN 'E' THEN 'EMPTY'
              WHEN 'X' THEN 'WELL_FORMED_XML' ELSE 'VALID_XML WITH SCHEMA COLLECTION '
              + QUOTENAME(SCHEMA_NAME(x.schema_id)) + '.' + QUOTENAME(x.name) END + ';' AS nvarchar(max)) AS Ddl
        FROM sys.service_message_types m
        LEFT JOIN sys.xml_schema_collections x ON x.xml_collection_id = m.xml_collection_id
        WHERE m.message_type_id > 65535
        UNION ALL
        SELECT 'Contracts', NULL, c.name COLLATE DATABASE_DEFAULT,
            'CREATE CONTRACT ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT) + ' AUTHORIZATION ' + QUOTENAME(USER_NAME(c.principal_id)) + ' ('
            + STUFF((SELECT ', ' + QUOTENAME(m.name COLLATE DATABASE_DEFAULT) + ' SENT BY '
                + CASE WHEN u.is_sent_by_initiator = 1 AND u.is_sent_by_target = 1 THEN 'ANY'
                       WHEN u.is_sent_by_initiator = 1 THEN 'INITIATOR' ELSE 'TARGET' END
                FROM sys.service_contract_message_usages u JOIN sys.service_message_types m ON m.message_type_id = u.message_type_id
                WHERE u.service_contract_id = c.service_contract_id ORDER BY m.name COLLATE DATABASE_DEFAULT
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ');'
        FROM sys.service_contracts c WHERE c.service_contract_id > 65535
        UNION ALL
        SELECT 'Queues', SCHEMA_NAME(q.schema_id), q.name,
            'CREATE QUEUE ' + QUOTENAME(SCHEMA_NAME(q.schema_id)) + '.' + QUOTENAME(q.name)
            + ' WITH STATUS = ' + CASE q.is_receive_enabled WHEN 1 THEN 'ON' ELSE 'OFF' END
            + ', RETENTION = ' + CASE q.is_retention_enabled WHEN 1 THEN 'ON' ELSE 'OFF' END
            + CASE WHEN q.activation_procedure IS NULL THEN '' ELSE ', ACTIVATION (STATUS = '
                + CASE q.is_activation_enabled WHEN 1 THEN 'ON' ELSE 'OFF' END
                + ', PROCEDURE_NAME = ' + q.activation_procedure
                + ', MAX_QUEUE_READERS = ' + CONVERT(varchar(10), q.max_readers)
                + ', EXECUTE AS ' + CASE WHEN q.execute_as_principal_id = -2 THEN 'OWNER'
                    ELSE QUOTENAME(USER_NAME(q.execute_as_principal_id), '''') END + ')' END
            + ', POISON_MESSAGE_HANDLING (STATUS = ' + CASE q.is_poison_message_handling_enabled WHEN 1 THEN 'ON' ELSE 'OFF' END + ');'
        FROM sys.service_queues q WHERE q.is_ms_shipped = 0
        UNION ALL
        SELECT 'Services', NULL, s.name COLLATE DATABASE_DEFAULT,
            'CREATE SERVICE ' + QUOTENAME(s.name COLLATE DATABASE_DEFAULT) + ' AUTHORIZATION ' + QUOTENAME(USER_NAME(s.principal_id))
            + ' ON QUEUE ' + QUOTENAME(SCHEMA_NAME(q.schema_id)) + '.' + QUOTENAME(q.name)
            + COALESCE(' (' + STUFF((SELECT ', ' + QUOTENAME(c.name COLLATE DATABASE_DEFAULT)
                FROM sys.service_contract_usages u JOIN sys.service_contracts c ON c.service_contract_id = u.service_contract_id
                WHERE u.service_id = s.service_id ORDER BY c.name COLLATE DATABASE_DEFAULT
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')', '') + ';'
        FROM sys.services s JOIN sys.service_queues q ON q.object_id = s.service_queue_id
        WHERE s.service_id > 65535 AND q.is_ms_shipped = 0
        ORDER BY Type, SchemaName, Name;
        """;
}

internal sealed class BrokerRow
{
    public string Type { get; set; } = "";
    public string? SchemaName { get; set; }
    public string Name { get; set; } = "";
    public string Ddl { get; set; } = "";
}
