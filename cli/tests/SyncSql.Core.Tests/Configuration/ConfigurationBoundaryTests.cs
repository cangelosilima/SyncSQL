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
