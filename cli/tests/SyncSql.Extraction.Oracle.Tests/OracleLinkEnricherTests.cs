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
    [InlineData("sql/instance/db/extra", null, null)]
    [InlineData(" /instance/db", null, null)]
    [InlineData("Server=sql", null, null)]
    [InlineData("sql;Password=secret", null, null)]
    [InlineData("sql;secret", null, null)]
    [InlineData("sql:invalid", null, null)]
    [InlineData("sql:99999999999999", null, null)]
    [InlineData("sql:0", null, null)]
    [InlineData("sql/", "sql", null)]
    [InlineData("sql//", "sql", null)]
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
            OracleQueries.ApplicationSchemas => FakeOracleDatabase.Rows(new { OWNER = "APP" }),
            OracleQueries.AllApplicationDatabaseLinks => throw FakeOracleDatabase.Error(942),
            OracleQueries.ApplicationDatabaseLinks => FakeOracleDatabase.Rows(new { OWNER = "APP", DB_LINK = "DL_ORDER", USERNAME = "entitlements", HOST = "GATEWAY" }),
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALIAS")]
    public async Task An_unresolved_dictionary_entry_preserves_what_is_known(string? identifier)
    {
        LinkMetadata link = await new OracleLinkEnricher(Server).EnrichAsync("APP", "DL", null, identifier, default);
        Assert.Equal(identifier, link.ConnectIdentifier);
        Assert.Null(link.DataSource);
        Assert.Empty(link.Logins);
    }

    [Theory]
    [InlineData("(DESCRIPTION=(ADDRESS=(HOST=ora))(CONNECT_DATA=(SERVICE_NAME=PDB)))", "ora:1521/PDB")]
    [InlineData("(DESCRIPTION=(ADDRESS=(HOST=ora)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=PDB)))", "ora:1522/PDB")]
    [InlineData("(DESCRIPTION=(ADDRESS=(HOST=ora)))", null)]
    [InlineData("(DESCRIPTION=(CONNECT_DATA=(SERVICE_NAME=PDB)))", null)]
    public async Task Native_descriptors_require_a_host_and_service(string descriptor, string? endpoint)
    {
        LinkMetadata link = await new OracleLinkEnricher(Server).EnrichAsync("APP", "DL", "", descriptor, default);
        Assert.Equal(endpoint, link.DataSource);
        Assert.Equal(endpoint is null, link.Diagnostics.Count > 0);
        if (endpoint is not null) { Assert.Equal(DatabaseEngine.Oracle, link.TargetEngine); }
    }

    [Theory]
    [InlineData("OTHER=(HOST=wrong)")]
    [InlineData("ALIAS=(HOST=one)\nALIAS=(HOST=two)")]
    [InlineData("ALIAS=(DESCRIPTION=(HOST=unfinished)")]
    public async Task Unmatched_duplicate_and_unbalanced_aliases_remain_unresolved(string text)
    {
        var server = Server with { OracleNetwork = new() { TnsNamesFile = Write("tnsnames.ora", text) } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader", "ALIAS", default);
        Assert.Null(link.DataSource);
        Assert.Contains(link.Diagnostics, d => d.Contains("not uniquely resolved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quoted_alias_values_and_comments_are_parsed_and_cached()
    {
        string tns = Write("tnsnames.ora", "# comment\nALIAS=(DESCRIPTION=(HOST='ora')(SERVICE_NAME=\"PDB#Dev\")) # tail");
        var enricher = new OracleLinkEnricher(Server with { OracleNetwork = new() { TnsNamesFile = tns } });
        LinkMetadata first = await enricher.EnrichAsync("APP", "DL", "reader", "ALIAS", default);
        File.Delete(tns);
        LinkMetadata cached = await enricher.EnrichAsync("APP", "OTHER", "reader", "ALIAS", default);
        Assert.Equal("ora:1521/PDB#Dev", first.DataSource);
        Assert.Equal(first.DataSource, cached.DataSource);
    }

    [Theory]
    [InlineData(null, "orders", null)]
    [InlineData("tg", null, "tg")]
    [InlineData("tg", "orders", "wrong")]
    [InlineData("tg", "other", "tg")]
    public async Task Gateway_configuration_requires_a_matching_host_and_sid(string? host, string? sid, string? configuredHost)
    {
        var server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", Host = configuredHost, InitFile = "unused" }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader", $"(DESCRIPTION=(HOST={host})(SID={sid})(HS=OK))", default);
        Assert.Null(link.DataSource);
        Assert.Contains(link.Diagnostics, d => d.Contains("matching gateway", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gateway_without_network_configuration_keeps_its_pointer()
    {
        LinkMetadata link = await new OracleLinkEnricher(Server).EnrichAsync("APP", "DL", "reader", "(DESCRIPTION=(HOST=tg)(SID=orders)(HS=OK))", default);
        Assert.Equal("tg", link.GatewayHost);
        Assert.NotEmpty(link.Diagnostics);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("HS_FDS_CONNECT_INFO=", null, false)]
    [InlineData("HS_FDS_CONNECT_INFO=one\nHS_FDS_CONNECT_INFO=two", null, false)]
    [InlineData("HS_FDS_CONNECT_INFO=dsn", null, true)]
    [InlineData("HS_FDS_CONNECT_INFO=dsn", "[other]\nServer=sql", true)]
    [InlineData("HS_FDS_CONNECT_INFO=dsn", "[dsn]\nServer=one\n[dsn]\nServer=two", true)]
    [InlineData("HS_FDS_CONNECT_INFO=dsn", "[dsn]\nDatabase=Orders", true)]
    [InlineData("HS_FDS_CONNECT_INFO=sql:invalid", null, false)]
    public async Task Incomplete_gateway_files_report_diagnostics(string? initText, string? odbcText, bool useOdbc)
    {
        string init = initText is null ? _root : Write("init.ora", initText);
        string? odbc = useOdbc ? odbcText is null ? Path.Combine(_root, "missing.ini") : Write("odbc.ini", odbcText) : null;
        var server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", Host = "TG", InitFile = init, OdbcIniFile = odbc }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader", "(DESCRIPTION=(HOST=tg)(SID=orders)(HS=OK))", default);
        Assert.Null(link.DataSource);
        Assert.NotEmpty(link.Diagnostics);
    }

    [Theory]
    [InlineData("Servername=sql\nDatabase=Orders", "sql")]
    [InlineData("Server=sql,1444\nPort=1433\n; ignored\nDatabase=Orders", "sql,1444")]
    public async Task Odbc_alternate_server_setting_and_existing_port_are_preserved(string settings, string expected)
    {
        var server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", InitFile = Write("init.ora", "HS_FDS_CONNECT_INFO=dsn"), OdbcIniFile = Write("odbc.ini", "[dsn]\n" + settings) }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader", "(DESCRIPTION=(HOST=tg)(SID=orders)(HS=OK))", default);
        Assert.Equal(expected, link.DataSource);
        Assert.Empty(link.Diagnostics);
    }

    [Fact]
    public async Task Non_sql_gateway_requires_an_explicit_mapping_without_odbc()
    {
        var server = Server with { OracleNetwork = new() { Gateways = [new() { Sid = "orders", TargetEngine = DatabaseEngine.Oracle, InitFile = Write("init.ora", "HS_FDS_CONNECT_INFO=dsn") }] } };
        LinkMetadata link = await new OracleLinkEnricher(server).EnrichAsync("APP", "DL", "reader", "(DESCRIPTION=(HOST=tg)(SID=orders)(HS=OK))", default);
        Assert.Null(link.DataSource);
        Assert.NotEmpty(link.Diagnostics);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
