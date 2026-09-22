using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Extraction.Oracle.Tests;

public sealed class OracleLinkEnricherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-link-").FullName;
    private static ServerConfig Server => new() { Name = "ORACLE", Host = "oracle", Type = DatabaseEngine.Oracle, ServiceName = "APP", CredentialsVariablePrefix = "ORA" };

    private string Write(string name, string text)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolves_gateway_and_odbc_sources_without_exporting_secrets(bool odbc)
    {
        string tns = Write("tnsnames.ora", "OTHER=(DESCRIPTION=(ADDRESS=(HOST=wrong)))\nGATEWAY_DEVELOPMENT, ANOTHER_ALIAS =\n (DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=tg)(PORT=1521))(CONNECT_DATA=(SID=orders))(HS=OK))");
        string init = Write("initorders.ora", $"HS_FDS_CONNECT_INFO={(odbc ? "orders_dsn" : "sql-dev:1444//Orders")}\nHS_FDS_RECOVERY_PWD=never-export-this\n");
        string? ini = odbc ? Write("odbc.ini", "[other]\nServer=wrong\n[orders_dsn]\nServer=sql-dev\nPort=1444\nDatabase=Orders\nPWD=never-export-this\n") : null;
        ServerConfig server = Server with { OracleNetwork = new() { TnsNamesFile = tns, Gateways = [new() { Sid = "orders", InitFile = init, OdbcIniFile = ini }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL_ORDER", "entitlements", "GATEWAY_DEVELOPMENT", default);
        Assert.Equal("sql-dev,1444", link.DataSource);
        Assert.Equal("Orders", link.Database);
        Assert.Equal("tg", link.GatewayHost);
        Assert.Equal("orders", link.GatewaySid);
        Assert.Equal(DatabaseEngine.MsSql, link.TargetEngine);
        Assert.Equal("entitlements", Assert.Single(link.Logins).RemoteUser);
        Assert.Empty(link.Diagnostics);
        Assert.Contains(link.Evidence, e => e.Field == "dataSource" && e.Source.StartsWith(odbc ? "odbc:" : "gateway:", StringComparison.Ordinal));
        Assert.DoesNotContain("never-export-this", JsonSerializer.Serialize(link));
    }

    [Fact]
    public async Task Missing_files_preserve_dictionary_pointer_and_explicit_owner_mapping()
    {
        ServerConfig server = Server with
        {
            OracleNetwork = new() { TnsNamesFile = Path.Combine(_root, "missing") },
            LinkTargets = [new() { Name = "DL_ORDER", Owner = "APP", TargetEngine = DatabaseEngine.MsSql, DataSource = "sql,1433", Database = "Orders" }],
        };
        var enricher = new OracleLinkEnricher(server);
        LinkMetadata mapped = await enricher.EnrichAsync("APP", "DL_ORDER", "entitlements", "GATEWAY", default);
        Assert.Equal("sql,1433", mapped.DataSource);
        Assert.NotEmpty(mapped.Diagnostics);
        LinkMetadata other = await enricher.EnrichAsync("OTHER", "DL_ORDER", "other", "GATEWAY", default);
        Assert.Null(other.DataSource);
        Assert.Equal("GATEWAY", other.ConnectIdentifier);
    }

    [Fact]
    public async Task Multiple_gateway_hosts_never_pick_a_destination()
    {
        string init = Write("initorders.ora", "HS_FDS_CONNECT_INFO=sql//Orders");
        ServerConfig server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", InitFile = init }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "user",
            "(DESCRIPTION=(ADDRESS=(HOST=one))(ADDRESS=(HOST=two))(CONNECT_DATA=(SID=orders))(HS=OK))", default);
        Assert.Null(link.DataSource);
        Assert.NotEmpty(link.Diagnostics);
    }

    [Fact]
    public async Task Quoted_gateway_database_name_is_not_truncated_as_a_comment()
    {
        string init = Write("initorders.ora", "HS_FDS_CONNECT_INFO=\"sql//Orders#Dev\" # comment");
        ServerConfig server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", InitFile = init }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader",
            "(DESCRIPTION=(ADDRESS=(HOST=tg))(CONNECT_DATA=(SID=orders))(HS=OK))", default);
        Assert.Equal("Orders#Dev", link.Database);
    }

    [Fact]
    public async Task Multiple_ports_never_fall_back_to_the_default_port()
    {
        LinkMetadata link = await new OracleLinkEnricher(Server).EnrichAsync("APP", "DL", "reader",
            "(DESCRIPTION=(ADDRESS=(HOST=ora)(PORT=1522))(ADDRESS=(HOST=ora)(PORT=1523))(CONNECT_DATA=(SERVICE_NAME=PDB)))", default);
        Assert.Null(link.DataSource);
        Assert.NotEmpty(link.Diagnostics);
    }

    [Theory]
    [InlineData("sql:1433//Orders", "sql,1433", "Orders")]
    [InlineData("sql/INSTANCE/Orders", "sql\\INSTANCE", "Orders")]
    [InlineData("[::1]:1444//Orders", "[::1],1444", "Orders")]
    [InlineData("sql", "sql", null)]
    [InlineData("sql:99999//Orders", null, null)]
    [InlineData("sql:1433/INSTANCE/Orders", null, null)]
    public void Dedicated_gateway_syntax_preserves_endpoint_qualifiers(string input, string? endpoint, string? database)
    {
        Assert.Equal((endpoint, database), OracleLinkEnricher.ParseSqlServerDestination(input));
    }

    [Fact]
    public async Task Denied_ddl_keeps_a_roundtrippable_link_pointer()
    {
        var db = new FakeOracleDatabase
        {
            Execute = (sql, _) => sql switch
        {
            OracleQueries.Schemas => FakeOracleDatabase.Rows(new { OWNER = "APP" }),
            OracleQueries.AllDatabaseLinks => throw FakeOracleDatabase.Error(942),
            OracleQueries.DatabaseLinks => FakeOracleDatabase.Rows(new { OWNER = "APP", DB_LINK = "DL_ORDER", USERNAME = "entitlements", HOST = "GATEWAY" }),
            OracleQueries.GetDdl => throw FakeOracleDatabase.Error(31603),
            _ => null,
        }
        };
        var extractor = new OracleObjectExtractor(NullLogger<OracleObjectExtractor>.Instance, TimeProvider.System, (_, _) => db);
        ServerConfig server = Server with { ObjectTypes = ["DatabaseLinks"] };
        ExtractionOutcome result = await extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server), new ExtractionOptions { Credentials = new("reader", "secret") }, default);
        ExtractedObject link = Assert.Single(result.Objects);
        var parsed = ExtractedObjectFile.Parse(ExtractedObjectFile.Write(link).Split('\n'));
        Assert.Equal("GATEWAY", parsed.Identity?.Link?.ConnectIdentifier);
        Assert.Equal("entitlements", Assert.Single(parsed.Identity!.Link!.Logins).RemoteUser);
        Assert.Contains("unavailable", parsed.Ddl);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
