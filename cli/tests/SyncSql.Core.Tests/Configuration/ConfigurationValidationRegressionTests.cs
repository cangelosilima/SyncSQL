using System.Text.Json.Nodes;
using SyncSql.Core.Configuration;

namespace SyncSql.Core.Tests.Configuration;

public sealed class ConfigurationValidationRegressionTests
{
    private const string ValidJson = """{"servers":[{"name":"SQL","host":"sql","type":"mssql","credentialsVariablePrefix":"SQL"}]}""";

    [Theory]
    [InlineData("server", "databases", "server 'SQL' databases")]
    [InlineData("server", "schemas", "server 'SQL' schemas")]
    [InlineData("server", "objectNames", "server 'SQL' objectNames")]
    [InlineData("defaults", "databases", "defaults.databases")]
    [InlineData("defaults", "schemas", "defaults.schemas")]
    [InlineData("defaults", "objectNames", "defaults.objectNames")]
    [InlineData("root", "serverSelection", "serverSelection")]
    public async Task InvalidRegexInEveryFilterLocationReportsItsLocation(string scope, string key, string location)
    {
        foreach (string list in new[] { "include", "exclude" })
        {
            JsonNode root = JsonNode.Parse(ValidJson)!;
            root["defaults"] = new JsonObject();
            JsonNode container = scope switch
            {
                "server" => root["servers"]![0]!,
                "defaults" => root["defaults"]!,
                _ => root,
            };
            container[key] = new JsonObject { [list] = new JsonArray("[") };
            ConfigValidationException error = await Assert.ThrowsAsync<ConfigValidationException>(() => Load(root.ToJsonString()));
            Assert.Contains("invalid regex", error.Message, StringComparison.Ordinal);
            Assert.Contains(location, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MatchingIncludeAndExcludeStillValidateInvalidRegex()
    {
        JsonNode root = JsonNode.Parse(ValidJson)!;
        root["serverSelection"] = new JsonObject { ["include"] = new JsonArray("["), ["exclude"] = new JsonArray("[") };
        await Assert.ThrowsAsync<ConfigValidationException>(() => Load(root.ToJsonString()));
    }

    [Fact]
    public async Task PropertiesAreCaseInsensitiveAndZeroDepthDisablesDiscovery()
    {
        SyncSqlConfig loaded = await Load("""{"SERVERS":[{"NAME":"SQL","HOST":"sql","TYPE":"mssql","CREDENTIALSVARIABLEPREFIX":"SQL"}],"discovery":{"linkedServers":{"enabled":true,"maxDepth":0}}}""");
        Assert.Equal("SQL", Assert.Single(loaded.Servers).Name);
        Assert.Equal(0, loaded.Discovery.LinkedServers.MaxDepth);
    }

    private static async Task<SyncSqlConfig> Load(string json)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            return await SyncSqlConfigLoader.LoadAsync(path);
        }
        finally { File.Delete(path); }
    }
}
