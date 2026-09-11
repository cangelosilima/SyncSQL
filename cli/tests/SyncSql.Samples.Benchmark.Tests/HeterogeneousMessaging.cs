using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SyncSql.Core.Domain;

namespace SyncSql.Samples.Benchmark.Tests;

internal static class HeterogeneousMessaging
{
    public static async Task AssertAsync(CancellationToken token)
    {
        foreach (var flow in new[]
        {
            (Server: "ATLAS_SQL", Database: "Commerce", Source: "ORDER_ENTRY", Audit: "ORDER_AUDIT"),
            (Server: "ATLAS_SQL", Database: "Receivables", Source: "INVOICING", Audit: "FINANCE_AUDIT"),
            (Server: "MERIDIAN_SQL", Database: "Distribution", Source: "INVENTORY", Audit: "STOCK_AUDIT"),
            (Server: "MERIDIAN_SQL", Database: "Intelligence", Source: "REPORTING", Audit: "REPORT_AUDIT"),
        })
        {
            var server = HeterogeneousFleet.Config.Servers.Single(s => s.Name == flow.Server);
            await using DbConnection admin = await HeterogeneousFleet.OpenAsync(server, flow.Database, null, token);
            Assert.True(Convert.ToBoolean(await HeterogeneousFleet.ScalarAsync(admin,
                "SELECT is_broker_enabled FROM sys.databases WHERE database_id = DB_ID()", token), CultureInfo.InvariantCulture));
            await HeterogeneousFleet.ExecuteAsync(admin, $"INSERT INTO {flow.Source}.ITEMS (ID, AMOUNT) VALUES (73001, 42);", token);
            await using DbConnection sender = await HeterogeneousFleet.OpenAsync(server, flow.Database, flow.Source + "_EXEC", token);
            Guid dialog = (Guid)(await HeterogeneousFleet.ScalarAsync(sender,
                $"DECLARE @dialog uniqueidentifier; EXEC {flow.Source}.P_SEND_EVENT @id = 73001, @dialog = @dialog OUTPUT; SELECT @dialog;", token))!;
            await using DbConnection denied = await HeterogeneousFleet.OpenAsync(server, flow.Database, flow.Source + "_READ", token);
            SqlException error = await Assert.ThrowsAsync<SqlException>(() => HeterogeneousFleet.ExecuteAsync(denied,
                $"DECLARE @dialog uniqueidentifier; EXEC {flow.Source}.P_SEND_EVENT @id = 73001, @dialog = @dialog OUTPUT;", token));
            Assert.Equal(229, error.Number);
            await using DbConnection receiver = await HeterogeneousFleet.OpenAsync(server, flow.Database, flow.Audit + "_EXEC", token);
            int delivered = 0;
            for (int attempt = 0; attempt < 6 && delivered == 0; attempt++)
            {
                await HeterogeneousFleet.ExecuteAsync(receiver, $"EXEC {flow.Audit}.P_RECEIVE_EVENT;", token);
                delivered = Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(admin,
                    $"SELECT COUNT(*) FROM {flow.Audit}.AUDIT_LOG WHERE ID = 73001 AND AMOUNT = 42;", token), CultureInfo.InvariantCulture);
            }
            Assert.Equal(1, delivered);
            // Complete the initiator's side of the normal EndDialog handshake.
            await HeterogeneousFleet.ExecuteAsync(admin, $"""
                DECLARE @handle uniqueidentifier, @type sysname;
                WAITFOR (RECEIVE TOP (1) @handle = conversation_handle, @type = message_type_name
                    FROM {flow.Source}.EVENT_OUTBOX WHERE conversation_handle = '{dialog}'), TIMEOUT 10000;
                IF @handle IS NULL OR @type <> N'http://schemas.microsoft.com/SQL/ServiceBroker/EndDialog'
                    THROW 51000, 'Broker EndDialog was not delivered', 1;
                END CONVERSATION @handle;
                DELETE FROM {flow.Source}.ITEMS WHERE ID = 73001;
                DELETE FROM {flow.Source}.AUDIT_LOG WHERE ID = 73001;
                DELETE FROM {flow.Audit}.AUDIT_LOG WHERE ID = 73001;
                """, token);
            Assert.Equal(0, Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(admin,
                "SELECT COUNT(*) FROM sys.transmission_queue", token), CultureInfo.InvariantCulture));
        }

        foreach (ExtractedObject publication in HeterogeneousDdl.LoadObjects().Where(o => o.Type == "Replication"))
        {
            var server = HeterogeneousFleet.Config.Servers.Single(s => s.Name == publication.Server);
            await using DbConnection connection = await HeterogeneousFleet.OpenAsync(server, publication.Database, null, token);
            Assert.Equal(2, Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(connection, $"""
                SELECT COUNT(*) FROM dbo.sysarticles a JOIN dbo.syspublications p ON p.pubid = a.pubid
                WHERE p.name = N'{publication.Name}' AND OBJECT_NAME(a.objid) = N'ITEMS';
                """, token), CultureInfo.InvariantCulture));
        }
    }
}
