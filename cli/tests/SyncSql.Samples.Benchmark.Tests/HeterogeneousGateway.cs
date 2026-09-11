using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Samples.Benchmark.Tests;

internal static class HeterogeneousGateway
{
    // Independent execution expectations: distinct values detect a wrong SID/database.
    internal static readonly GatewayDestination[] Destinations =
    [
        new("ATLAS_SQL", "Commerce", "ORDER_ENTRY", "PROCUREMENT", "REMOTE", 111),
        new("ATLAS_SQL", "Receivables", "INVOICING", "PROCUREMENT", "RECEIVABLES", 222),
        new("MERIDIAN_SQL", "Distribution", "INVENTORY", "COMPLIANCE", "REMOTE", 333),
        new("MERIDIAN_SQL", "Intelligence", "REPORTING", "COMPLIANCE", "INTELLIGENCE", 444),
    ];

    internal static async Task ProvisionBridgeAsync(CancellationToken token)
    {
        string password = HeterogeneousFleet.Password.Replace("'", "''", StringComparison.Ordinal);
        foreach (ServerConfig server in HeterogeneousFleet.Config.Servers.Where(s => s.Type == DatabaseEngine.MsSql))
        {
            await using DbConnection admin = await HeterogeneousFleet.OpenAsync(server, null, null, token);
            await HeterogeneousFleet.ExecuteAsync(admin,
                $"IF SUSER_ID(N'bridge_user') IS NOT NULL DROP LOGIN [bridge_user]; CREATE LOGIN [bridge_user] WITH PASSWORD=N'{password}', CHECK_POLICY=OFF;", token);
            string remote = server.Name == "ATLAS_SQL" ? "MERIDIAN_SQL" : "ATLAS_SQL";
            // Override the admin fixture mapping for this least-privileged bridge login.
            await HeterogeneousFleet.ExecuteAsync(admin,
                $"EXEC master.dbo.sp_addlinkedsrvlogin @rmtsrvname=N'{remote}', @useself='false', @locallogin=N'bridge_user', @rmtuser=N'bridge_user', @rmtpassword=N'{password}';", token);
            foreach (GatewayDestination destination in Destinations.Where(d => d.Server == server.Name))
            {
                await using DbConnection database = await HeterogeneousFleet.OpenAsync(server, destination.Database, null, token);
                await HeterogeneousFleet.ExecuteAsync(database,
                    $"CREATE USER [bridge_user] FOR LOGIN [bridge_user]; " +
                    $"GRANT SELECT ON OBJECT::{destination.Schema}.ITEMS TO [bridge_user]; " +
                    $"DENY SELECT ON OBJECT::{destination.Schema}.AUDIT_LOG TO [bridge_user]; " +
                    $"INSERT INTO {destination.Schema}.ITEMS (ID, AMOUNT) VALUES (9001, {destination.Amount});", token);
                if (destination.Database == "Commerce")
                {
                    await HeterogeneousFleet.ExecuteAsync(database,
                        "GRANT SELECT ON OBJECT::ORDER_ENTRY.V_REMOTE_STOCK TO [bridge_user];", token);
                }
            }
        }
    }

    internal static async Task AssertExecutionAsync(CancellationToken token)
    {
        ServerConfig oracle = HeterogeneousFleet.Config.Servers.Single(s => s.Type == DatabaseEngine.Oracle);
        foreach (GatewayDestination destination in Destinations)
        {
            await using DbConnection connection = await HeterogeneousFleet.OpenAsync(oracle, null, destination.Owner, token);
            Assert.Equal(destination.Amount, await AmountAsync(connection,
                $"{destination.Schema}.ITEMS@{destination.Link}", token));

            // A network error must never count as an expected authorization failure.
            OracleException denied = await Assert.ThrowsAsync<OracleException>(() => HeterogeneousFleet.ScalarAsync(connection,
                $"SELECT COUNT(*) FROM {destination.Schema}.AUDIT_LOG@{destination.Link}", token));
            // Gateways can map authorization errors to Oracle codes or retain the
            // native SQL Server 229 inside ORA-28500. The direct SQL checks above
            // already prove that AUDIT_LOG exists and that this login is denied.
            Assert.True(denied.Number is 942 or 1031
                || (denied.Number == 28500 && denied.Message.Contains("229", StringComparison.Ordinal)),
                $"Expected remote authorization denial, received ORA-{denied.Number}: {denied.Message}");
        }
        await using (DbConnection procurement = await HeterogeneousFleet.OpenAsync(oracle, null, "PROCUREMENT", token))
        {
            Assert.Equal(333, await AmountAsync(procurement, "INVENTORY.ITEMS@SECONDARY", token));
            Assert.Equal(333, await AmountAsync(procurement, "ORDER_ENTRY.V_REMOTE_STOCK@REMOTE", token));
        }
        await using (DbConnection compliance = await HeterogeneousFleet.OpenAsync(oracle, null, "COMPLIANCE", token))
        {
            Assert.Equal(111, await AmountAsync(compliance, "ORDER_ENTRY.ITEMS@SECONDARY", token));
        }
        // Calls run under workload accounts, exercising definer-rights private links.
        foreach ((string owner, string procedure) in new[]
        {
            ("PROCUREMENT", "P_ONE"), ("PROCUREMENT", "P_TWO"), ("PROCUREMENT", "P_RECEIVABLES"),
            ("PROCUREMENT", "P_STOCK"), ("COMPLIANCE", "P_ONE"), ("COMPLIANCE", "P_INTELLIGENCE"),
        })
        {
            await using DbConnection executor = await HeterogeneousFleet.OpenAsync(oracle, null, owner + "_EXEC", token);
            await HeterogeneousFleet.ExecuteAsync(executor, $"BEGIN {owner}.{procedure}; END;", token);
            await using DbConnection reader = await HeterogeneousFleet.OpenAsync(oracle, null, owner + "_READ", token);
            OracleException denied = await Assert.ThrowsAsync<OracleException>(() =>
                HeterogeneousFleet.ExecuteAsync(reader, $"BEGIN {owner}.{procedure}; END;", token));
            Assert.Equal(6550, denied.Number);
            Assert.Contains("PLS-00201", denied.Message, StringComparison.Ordinal);
        }
    }

    internal static async Task AssertBridgePermissionsAsync(CancellationToken token)
    {
        foreach (GatewayDestination destination in Destinations)
        {
            ServerConfig server = HeterogeneousFleet.Config.Servers.Single(s => s.Name == destination.Server);
            await using DbConnection bridge = await HeterogeneousFleet.OpenAsync(server, destination.Database, "bridge_user", token);
            Assert.Equal(destination.Amount, await AmountAsync(bridge, destination.Schema + ".ITEMS", token));
            SqlException denied = await Assert.ThrowsAsync<SqlException>(() => HeterogeneousFleet.ScalarAsync(bridge,
                $"SELECT COUNT(*) FROM {destination.Schema}.AUDIT_LOG", token));
            Assert.Equal(229, denied.Number);
            SqlException writeDenied = await Assert.ThrowsAsync<SqlException>(() => HeterogeneousFleet.ExecuteAsync(bridge,
                $"UPDATE {destination.Schema}.ITEMS SET AMOUNT=0 WHERE ID=9001", token));
            Assert.Equal(229, writeDenied.Number);
        }
        ServerConfig atlas = HeterogeneousFleet.Config.Servers.Single(s => s.Name == "ATLAS_SQL");
        await using DbConnection linked = await HeterogeneousFleet.OpenAsync(atlas, "Commerce", "bridge_user", token);
        Assert.Equal(333, await AmountAsync(linked, "ORDER_ENTRY.V_REMOTE_STOCK", token));
    }

    private static async Task<int> AmountAsync(DbConnection connection, string source, CancellationToken token) =>
        Convert.ToInt32(await HeterogeneousFleet.ScalarAsync(connection,
            $"SELECT SUM(AMOUNT) FROM {source} WHERE ID=9001", token), CultureInfo.InvariantCulture);
}

internal sealed record GatewayDestination(string Server, string Database, string Schema, string Owner, string Link, int Amount);
