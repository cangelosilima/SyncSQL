using System.Text.Json;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public sealed class ConfigurationBoundaryTests
{
    private static readonly ServerConfig Server = new()
    {
        Name = "local",
        Host = "local",
        Type = DatabaseEngine.MsSql,
        CredentialsVariablePrefix = "TEST",
    };

    [Theory]
    [InlineData("name")]
    [InlineData("host")]
    [InlineData("credentialsVariablePrefix")]
    public async Task BlankRequiredValue_ReportsTheKey(string key)
    {
        var values = new Dictionary<string, string>
        {
            ["name"] = "server",
            ["host"] = "host",
            ["type"] = "mssql",
            ["credentialsVariablePrefix"] = "TEST",
        };
        values[key] = " ";
        await Reject(JsonSerializer.Serialize(new { servers = new[] { values } }), key);
    }

    [Fact]
    public async Task NullConfiguration_IsRejected() => await Reject("null", "empty or 'null'");

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    [InlineData("[\" \" ]")]
    public async Task EmptyAliases_AreRejected(string aliases) => await Reject(
        $$"""{"servers":[{"name":"server","host":"host","type":"mssql","credentialsVariablePrefix":"TEST","aliases":{{aliases}}}]}""",
        "empty alias");

    [Fact]
    public void Discovery_DoesNotTreatAnOracleAliasAsAnExtractedSqlServer()
    {
        var link = new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = "remote" };
        var oracle = Server with { Name = "oracle", Type = DatabaseEngine.Oracle, Host = "remote", Aliases = ["remote,1433"] };
        var plan = LinkedServerFollowUpPlanner.Plan(Server, "user", [link], new LinkedServerDiscoveryConfig { Enabled = true }, [Server, oracle]);
        Assert.Equal(DatabaseEngine.MsSql, Assert.Single(plan.FollowUps).Server.Type);
        Assert.Empty(plan.Skipped);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(254)]
    public async Task OversizedDnsSuffix_IsRejected(int length) => await Reject(
        JsonSerializer.Serialize(new SyncSqlConfig { Servers = [Server with { HostNameSuffix = new string('a', length) }] }),
        "hostNameSuffix");

    private static async Task Reject(string json, string message)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            var error = await Assert.ThrowsAsync<ConfigValidationException>(() => SyncSqlConfigLoader.LoadAsync(path));
            Assert.Contains(message, error.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("\"linkTargets\":null")]
    [InlineData("\"linkTargets\":[{\"name\":\" \"}]")]
    [InlineData("\"linkTargets\":[{\"name\":\"DL\"},{\"name\":\"dl\"}]")]
    [InlineData("\"oracleNetwork\":{\"gateways\":null}")]
    [InlineData("\"oracleNetwork\":{\"gateways\":[{\"sid\":\"\",\"initFile\":\"init.ora\"}]}")]
    [InlineData("\"oracleNetwork\":{\"gateways\":[{\"sid\":\"orders\",\"initFile\":\" \"}]}")]
    public async Task Invalid_enrichment_configuration_is_rejected(string property) => await Reject(
        $$"""{"servers":[{"name":"server","host":"host","type":"mssql","credentialsVariablePrefix":"TEST",{{property}}}]}""",
        "link enrichment");

    [Fact]
    public async Task Optional_network_file_paths_remain_optional()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SyncSqlConfig
            {
                Servers = [Server with
            {
                OracleNetwork = new() { Gateways = [new() { Sid = "orders", InitFile = "init.ora" }] },
            }]
            }));
            ServerConfig loaded = Assert.Single((await SyncSqlConfigLoader.LoadAsync(path)).Servers);
            Assert.Null(loaded.OracleNetwork!.TnsNamesFile);
            Assert.Null(Assert.Single(loaded.OracleNetwork.Gateways).OdbcIniFile);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Partial_and_ownerless_overrides_keep_discovered_values_and_evidence()
    {
        LinkMetadata original = new()
        {
            DataSource = "observed",
            Database = "Orders",
            DefaultSchema = "sales",
            TargetEngine = DatabaseEngine.MsSql,
            Evidence = [new("dataSource", "observed", "dictionary")]
        };
        ServerConfig configured = Server with { LinkTargets = [new() { Name = "DL" }, new() { Name = "Other", Owner = "APP", DataSource = "unrelated" }] };
        LinkMetadata unchanged = LinkTargetEnrichment.Apply(configured, "dl", "APP", original);
        Assert.Equal(original.DataSource, unchanged.DataSource);
        Assert.Equal(original.Database, unchanged.Database);
        Assert.Equal(original.DefaultSchema, unchanged.DefaultSchema);
        Assert.Equal(original.TargetEngine, unchanged.TargetEngine);
        Assert.Equal(original.Evidence, unchanged.Evidence);
        LinkMetadata schema = LinkTargetEnrichment.Apply(configured with { LinkTargets = [new() { Name = "DL", DefaultSchema = "archive" }] }, "DL", null, original);
        Assert.Equal("archive", schema.DefaultSchema);
        Assert.Equal("observed", schema.DataSource);
        Assert.Equal("Orders", schema.Database);
        Assert.Equal(DatabaseEngine.MsSql, schema.TargetEngine);
        Assert.Contains(schema.Evidence, e => e.Field == "defaultSchema" && e.Value == "archive");
        Assert.Contains(original.Evidence[0], schema.Evidence);
        Assert.Same(original, LinkTargetEnrichment.Apply(configured with { LinkTargets = [new() { Name = "DL" }, new() { Name = "DL" }] }, "DL", "APP", original));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Blank_optional_mappings_preserve_discovered_context(string? blank)
    {
        LinkMetadata original = new() { DataSource = "sql,1433", Database = "Orders", DefaultSchema = "sales", TargetEngine = DatabaseEngine.MsSql };
        LinkMetadata result = LinkTargetEnrichment.Apply(Server with { LinkTargets = [new() { Name = "DL", DataSource = blank, Database = blank, DefaultSchema = blank }] }, "DL", "APP", original);
        Assert.Equal(original.DataSource, result.DataSource);
        Assert.Equal(original.Database, result.Database);
        Assert.Equal(original.DefaultSchema, result.DefaultSchema);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Exact_owner_mapping_overrides_discovered_endpoint_and_engine()
    {
        LinkMetadata original = new() { DataSource = "placeholder", Database = "Orders", DefaultSchema = "sales", TargetEngine = DatabaseEngine.MsSql };
        ServerConfig server = Server with
        {
            LinkTargets = [new() { Name = "DL", DataSource = "generic" },
            new() { Name = "DL", Owner = "APP", DataSource = "oracle:1521/PDB", Database = "PDB", DefaultSchema = "APP", TargetEngine = DatabaseEngine.Oracle }]
        };
        LinkMetadata result = LinkTargetEnrichment.Apply(server, "DL", "APP", original);
        Assert.Equal("oracle:1521/PDB", result.DataSource);
        Assert.Equal(DatabaseEngine.Oracle, result.TargetEngine);
        Assert.Equal("PDB", result.Database);
        Assert.Equal("APP", result.DefaultSchema);
        Assert.Equal(4, result.Evidence.Count);
    }

    [Fact]
    public void EffectivePort_UsesEngineDefaultsAndRejectsUnknownEngine()
    {
        Assert.Equal(1433, Server.EffectivePort);
        Assert.Equal(1521, (Server with { Type = DatabaseEngine.Oracle }).EffectivePort);
        Assert.Equal(1444, (Server with { Port = 1444 }).EffectivePort);
        Assert.Throws<ArgumentOutOfRangeException>(() => (Server with { Type = (DatabaseEngine)999 }).EffectivePort);
    }

    [Theory]
    [InlineData(null, "no data source")]
    [InlineData(" ", "no data source")]
    [InlineData(",1433", "no host part")]
    public void Discovery_RejectsMissingHost(string? source, string reason)
    {
        var plan = Plan(new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = source });
        Assert.Empty(plan.FollowUps);
        Assert.Contains(reason, Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("MSOLEDBSQL", true)]
    [InlineData("SQLOLEDB", true)]
    [InlineData("SQLNCLI11", true)]
    [InlineData("other", false)]
    public void Discovery_RecognizesSqlProviders(string? provider, bool expected)
    {
        var plan = Plan(new DiscoveredLinkedServer { Name = "remote", Provider = provider, DataSource = "remote" });
        Assert.Equal(expected ? 1 : 0, plan.FollowUps.Count);
    }

    [Theory]
    [InlineData("remote,bad")]
    [InlineData("remote,0")]
    [InlineData("remote,-1")]
    public void Discovery_InvalidPortUsesParentDefault(string source)
    {
        var plan = Plan(new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = source });
        Assert.Equal(1433, Assert.Single(plan.FollowUps).Server.EffectivePort);
    }

    [Fact]
    public void Discovery_OracleParentDoesNotPassItsPortToSqlServer()
    {
        var plan = Plan(new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = "remote" },
            Server with { Type = DatabaseEngine.Oracle, Port = 1521 });
        Assert.Null(Assert.Single(plan.FollowUps).Server.Port);
    }

    [Theory]
    [InlineData("lpc:remote", "lpc:remote.example.com")]
    [InlineData("remote-name_2", "remote-name_2.example.com")]
    public void Discovery_QualifiesTransportAndDnsNames(string source, string expected)
    {
        var plan = Plan(new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = source },
            Server with { HostNameSuffix = "example.com" });
        Assert.Equal(expected, Assert.Single(plan.FollowUps).Server.Host);
    }

    [Fact]
    public void Discovery_SanitizesEmptyNamesAndSkipsEveryOccupiedSuffix()
    {
        var link = new DiscoveredLinkedServer { Name = " ", Product = "SQL Server", DataSource = "remote" };
        var config = new LinkedServerDiscoveryConfig { Enabled = true };
        var plan = LinkedServerFollowUpPlanner.Plan(Server, "user", [link], config,
            [Server with { Name = "LinkedServer" }, Server with { Name = "LinkedServer_2" }]);
        Assert.Equal("LinkedServer_3", Assert.Single(plan.FollowUps).Server.Name);
        var sanitized = Plan(link with { Name = "a/b,c\\d" });
        Assert.Equal("a_b_c_d", Assert.Single(sanitized.FollowUps).Server.Name);
    }

    private static LinkedServerFollowUpPlan Plan(DiscoveredLinkedServer link, ServerConfig? parent = null) =>
        LinkedServerFollowUpPlanner.Plan(parent ?? Server, "user", [link], new LinkedServerDiscoveryConfig { Enabled = true }, [Server]);
}
