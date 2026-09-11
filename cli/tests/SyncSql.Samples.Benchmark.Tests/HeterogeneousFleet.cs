using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>Provisions only the three named, local Docker hosts. Never reads expected-catalog.json.</summary>
internal sealed class HeterogeneousFleet
{
    public static bool Enabled => Environment.GetEnvironmentVariable("SYNCSQL_HETEROGENEOUS") == "1";
    public static bool GatewayEnabled => Environment.GetEnvironmentVariable("SYNCSQL_HETEROGENEOUS_GATEWAY") == "1";
    public static string ConfigPath => Path.Combine(HeterogeneousContract.Root, "servers.json");
    public static SyncSqlConfig Config => JsonSerializer.Deserialize<SyncSqlConfig>(File.ReadAllText(ConfigPath), SyncSqlJsonOptions.Default)!;
    public static string Password => Environment.GetEnvironmentVariable("BENCH_PASSWORD")
        ?? throw new InvalidOperationException("BENCH_PASSWORD is required. Use the scenario runner.");
    public static List<ScenarioPrincipal> Principals => JsonSerializer.Deserialize<List<ScenarioPrincipal>>(
        File.ReadAllText(Path.Combine(HeterogeneousContract.Root, "principals.json")), SyncSqlJsonOptions.Default)!;

    public async Task ProvisionAsync(CancellationToken token)
    {
        IReadOnlyList<ExtractedObject> objects = HeterogeneousDdl.LoadObjects();
        foreach (ServerConfig server in Config.Servers)
        {
            await using DbConnection admin = await OpenAsync(server, null, null, token);
            string machine = Convert.ToString(await ScalarAsync(admin,
                server.Type == DatabaseEngine.MsSql ? "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('MachineName'))"
                    : "SELECT SYS_CONTEXT('USERENV','SERVER_HOST') FROM DUAL", token), CultureInfo.InvariantCulture)!;
            if (!machine.Equals(server.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to provision {server.Name}: connected to host {machine}.");
            }

            ExtractedObject[] source = [.. objects.Where(o => o.Server == server.Name)];
            if (server.Type == DatabaseEngine.MsSql)
            {
                await ProvisionSqlAsync(server, admin, source, token);
            }
            else
            {
                await ProvisionOracleAsync(server, admin, source, token);
            }
        }
        // A distributed view binds against the other SQL instance at CREATE time.
        // Install it only after both instances have their tables and links.
        ExtractedObject remoteView = objects.Single(o => o.Name == "V_REMOTE_STOCK");
        await using DbConnection viewConnection = await OpenAsync(Config.Servers.Single(s => s.Name == remoteView.Server), remoteView.Database, null, token);
        await ExecuteAsync(viewConnection, remoteView.Ddl, token);
        await ApplyGrantsAsync(viewConnection, remoteView, token);
        await HeterogeneousGateway.ProvisionBridgeAsync(token);
    }

    private static async Task ProvisionSqlAsync(ServerConfig server, DbConnection admin, ExtractedObject[] objects, CancellationToken token)
    {
        // The hostname guard above and fixed config restrict resets to this disposable fleet.
        string[] databases = [.. objects.Where(o => o.Database != "_ServerLevel").Select(o => o.Database).Distinct()];
        foreach (string database in databases)
        {
            await ExecuteAsync(admin, $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END; CREATE DATABASE [{database}];", token);
        }
        foreach (ScenarioPrincipal user in Principals.Where(p => p.Server == server.Name))
        {
            await ExecuteAsync(admin, $"IF SUSER_ID(N'{user.Name}') IS NOT NULL DROP LOGIN [{user.Name}]; CREATE LOGIN [{user.Name}] WITH PASSWORD = N'{SqlPassword}', CHECK_POLICY = OFF;", token);
        }
        foreach (string database in databases)
        {
            await using DbConnection connection = await OpenAsync(server, database, null, token);
            foreach (string schema in objects.Where(o => o.Database == database).Select(o => o.Schema!).Distinct())
            {
                await ExecuteAsync(connection, $"CREATE SCHEMA [{schema}] AUTHORIZATION dbo;", token);
            }

            foreach (ScenarioPrincipal user in Principals.Where(p => p.Server == server.Name && p.Database == database))
            {
                await ExecuteAsync(connection, $"CREATE USER [{user.Name}] FOR LOGIN [{user.Name}];", token);
            }
        }
        foreach (ExtractedObject link in objects.Where(o => o.Type == "LinkedServers"))
        {
            await ExecuteAsync(admin, $"IF EXISTS (SELECT 1 FROM sys.servers WHERE name = N'{link.Name}') EXEC master.dbo.sp_dropserver @server=N'{link.Name}', @droplogins='droplogins';", token);
            // Linux rejects Oracle OLE DB registration, even without opening the link.
            // Register a visibly labelled SQL-provider transport placeholder: the actual
            // destination identity remains Oracle, but no runtime connectivity is claimed.
            string linkDdl = link.Name == "HELIOS_ORACLE"
                ? link.Ddl.Replace("N'OraOLEDB.Oracle'", "N'MSOLEDBSQL'", StringComparison.Ordinal)
                    .Replace("@srvproduct = N'Oracle'", "@srvproduct = N'Oracle (lineage metadata only)'", StringComparison.Ordinal)
                : link.Ddl;
            await ExecuteAsync(admin, linkDdl, token);
            await ExecuteAsync(admin, $"EXEC master.dbo.sp_serveroption @server=N'{link.Name}', @optname='rpc out', @optvalue='true';", token);
            // Account mappings are install metadata too. No remote query is executed here.
            string remoteUser = link.Name == "HELIOS_ORACLE" ? "PROCUREMENT" : "sa";
            await ExecuteAsync(admin, $"EXEC master.dbo.sp_addlinkedsrvlogin @rmtsrvname=N'{link.Name}', @useself='false', @rmtuser=N'{remoteUser}', @rmtpassword=N'{SqlPassword}';", token);
        }
        foreach (ExtractedObject obj in objects.Where(o => o.Type != "LinkedServers" && o.Name != "V_REMOTE_STOCK").OrderBy(Phase))
        {
            await using DbConnection connection = await OpenAsync(server, obj.Database, null, token);
            await ExecuteAsync(connection, obj.Ddl, token);
            await ApplyGrantsAsync(connection, obj, token);
        }
    }

    private static async Task ProvisionOracleAsync(ServerConfig server, DbConnection admin, ExtractedObject[] objects, CancellationToken token)
    {
        string[] owners = [.. objects.Select(o => o.Schema!).Distinct()];
        foreach (string user in owners.Concat(Principals.Where(p => p.Server == server.Name).Select(p => p.Name)))
        {
            await ExecuteAsync(admin, $"BEGIN EXECUTE IMMEDIATE 'DROP USER {user} CASCADE'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1918 THEN RAISE; END IF; END;", token);
            await ExecuteAsync(admin, $"CREATE USER {user} IDENTIFIED BY \"{OraclePassword}\" DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS", token);
            await ExecuteAsync(admin, $"GRANT CREATE SESSION TO {user}", token);
        }
        foreach (string owner in owners)
        {
            await ExecuteAsync(admin, $"GRANT CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE TRIGGER, CREATE DATABASE LINK TO {owner}", token);
        }

        foreach (ExtractedObject obj in objects.OrderBy(Phase))
        {
            await using DbConnection connection = await OpenAsync(server, null, obj.Schema, token);
            string sql = obj.Ddl.Replace("__BENCH_PASSWORD__", OraclePassword, StringComparison.Ordinal);
            if (obj.Type is "Tables" or "Views" or "DatabaseLinks")
            {
                sql = sql.TrimEnd().TrimEnd(';');
            }

            await ExecuteAsync(connection, sql, token);
            if (obj.Type != "PackageBodies")
            {
                await ApplyGrantsAsync(connection, obj, token);
            }

            if (obj.Schema == "COMPLIANCE" && obj.Type is "Views" or "Procedures" or "Packages")
            {
                await ExecuteAsync(connection, $"GRANT {(obj.Type == "Views" ? "SELECT" : "EXECUTE")} ON {obj.Schema}.{obj.Name} TO PROCUREMENT", token);
            }
        }
        // A declaration can compile before its referenced package has been installed.
        foreach (string owner in owners)
        {
            await ExecuteAsync(admin, $"BEGIN DBMS_UTILITY.COMPILE_SCHEMA(schema => '{owner}', compile_all => FALSE); END;", token);
        }

        long errors = Convert.ToInt64(await ScalarAsync(admin,
            "SELECT COUNT(*) FROM DBA_OBJECTS WHERE OWNER IN ('PROCUREMENT','COMPLIANCE') AND STATUS <> 'VALID'", token), CultureInfo.InvariantCulture);
        Assert.Equal(0, errors);
    }

    private static int Phase(ExtractedObject obj) => obj.Type switch
    {
        "Tables" => 0,
        "DatabaseLinks" => 1,
        "Views" when obj.Name == "V_ITEMS" => 2,
        "Views" => 3,
        "Packages" => 4,
        "PackageBodies" => 5,
        "Functions" => 6,
        "Procedures" or "StoredProcedures" => 7,
        _ => 8,
    };

    private static async Task ApplyGrantsAsync(DbConnection connection, ExtractedObject obj, CancellationToken token)
    {
        foreach (GrantEntry grant in obj.Grants)
        {
            string column = grant.Column is null ? "" : $" ({grant.Column})";
            string sql = obj.Engine == DatabaseEngine.Oracle
                ? $"GRANT {grant.Permission}{column} ON {obj.Schema}.{obj.Name} TO {grant.Grantee}"
                : $"{(grant.State == GrantState.Deny ? "DENY" : "GRANT")} {grant.Permission} ON OBJECT::{obj.Schema}.{obj.Name}{column} TO [{grant.Grantee}];";
            await ExecuteAsync(connection, sql, token);
        }
    }

    private static string SqlPassword => Password.Replace("'", "''", StringComparison.Ordinal);
    private static string OraclePassword => Password.Replace("\"", "\"\"", StringComparison.Ordinal);

    public static async Task<DbConnection> OpenAsync(ServerConfig server, string? database, string? user, CancellationToken token)
    {
        DbConnection connection;
        if (server.Type == DatabaseEngine.MsSql)
        {
            SqlConnectionStringBuilder builder = new()
            {
                DataSource = $"tcp:127.0.0.1,{server.Port}",
                InitialCatalog = database ?? "master",
                UserID = user ?? "sa",
                Password = Password,
                TrustServerCertificate = true,
                Encrypt = true,
                Pooling = false,
                ConnectTimeout = 30,
            };
            connection = new SqlConnection(builder.ConnectionString);
        }
        else
        {
            OracleConnectionStringBuilder builder = new()
            {
                DataSource = $"127.0.0.1:{server.Port}/FREEPDB1",
                UserID = user ?? "SYSTEM",
                Password = Password,
                Pooling = false,
            };
            connection = new OracleConnection(builder.ConnectionString);
        }
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken token)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(token);
    }

    public static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken token)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        return await command.ExecuteScalarAsync(token);
    }
}

internal sealed record ScenarioPrincipal(string Server, string Database, string Name);
