using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public class SyncSqlConfigLoaderTests
{
    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync("/nonexistent/servers.json"));
    }

    [Fact]
    public async Task LoadAsync_NoServers_Throws()
    {
        string path = await WriteTempConfigAsync("""{"servers":[]}""");

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("does not define any servers", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_DuplicateServerNames_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {"servers":[
              {"name":"SQLPROD01","type":"mssql","host":"h1","credentialsVariablePrefix":"A"},
              {"name":"sqlprod01","type":"mssql","host":"h2","credentialsVariablePrefix":"B"}
            ]}
            """);

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_OracleServerWithoutServiceName_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {"servers":[{"name":"ORAPROD01","type":"oracle","host":"h","credentialsVariablePrefix":"ORA"}]}
            """);

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("serviceName", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_InvalidRegexInFilter_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {
              "defaults": { "objectNames": { "include": ["[unclosed"] } },
              "servers":[{"name":"SQLPROD01","type":"mssql","host":"h","credentialsVariablePrefix":"A"}]
            }
            """);

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("invalid regex", ex.Message);
        Assert.Contains("[unclosed", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_UnknownEngineType_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {"servers":[{"name":"X","type":"postgres","host":"h","credentialsVariablePrefix":"X"}]}
            """);

        await Assert.ThrowsAsync<ConfigValidationException>(() => SyncSqlConfigLoader.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_NoDiscoveryBlock_DefaultsToNotFollowingLinkedServers()
    {
        string path = await WriteTempConfigAsync("""
            {"servers":[{"name":"S","type":"mssql","host":"h","credentialsVariablePrefix":"S"}]}
            """);

        SyncSqlConfig config = await SyncSqlConfigLoader.LoadAsync(path);

        Assert.False(config.Discovery.LinkedServers.Enabled);
        Assert.Equal(1, config.Discovery.LinkedServers.MaxDepth);
        Assert.True(config.Discovery.LinkedServers.RequireMatchingLogin);
        Assert.True(config.Discovery.LinkedServers.RestrictToLinkedCatalog);
    }

    [Fact]
    public async Task LoadAsync_DiscoveryBlock_IsParsed()
    {
        string path = await WriteTempConfigAsync("""
            {
              "discovery":{"linkedServers":{"enabled":true,"maxDepth":2,"requireMatchingLogin":false,"linkNames":{"exclude":["^TEMP_"]}}},
              "servers":[{"name":"S","type":"mssql","host":"h","credentialsVariablePrefix":"S"}]
            }
            """);

        SyncSqlConfig config = await SyncSqlConfigLoader.LoadAsync(path);

        Assert.True(config.Discovery.LinkedServers.Enabled);
        Assert.Equal(2, config.Discovery.LinkedServers.MaxDepth);
        Assert.False(config.Discovery.LinkedServers.RequireMatchingLogin);
        Assert.False(config.Discovery.LinkedServers.LinkNames.IsAllowed("TEMP_LINK"));
    }

    [Fact]
    public async Task LoadAsync_NegativeDiscoveryDepth_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {
              "discovery":{"linkedServers":{"enabled":true,"maxDepth":-1}},
              "servers":[{"name":"S","type":"mssql","host":"h","credentialsVariablePrefix":"S"}]
            }
            """);

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("maxDepth", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_InvalidRegexInDiscoveryLinkNames_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {
              "discovery":{"linkedServers":{"linkNames":{"include":["["]}}},
              "servers":[{"name":"S","type":"mssql","host":"h","credentialsVariablePrefix":"S"}]
            }
            """);

        ConfigValidationException ex = await Assert.ThrowsAsync<ConfigValidationException>(
            () => SyncSqlConfigLoader.LoadAsync(path));
        Assert.Contains("discovery.linkedServers.linkNames", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_ValidConfig_ParsesServersAndFilters()
    {
        string path = await WriteTempConfigAsync("""
            {
              "git": { "branch": "main", "pathPrefix": "objects" },
              "defaults": {
                "databases": { "exclude": ["^tempdb$"] },
                "objectTypes": ["Tables", "Views"]
              },
              "servers": [
                {
                  "name": "SQLPROD01",
                  "type": "mssql",
                  "host": "sqlprod01.example.com",
                  "port": 1433,
                  "credentialsVariablePrefix": "SQLPROD01"
                },
                {
                  "name": "ORAPROD01",
                  "type": "oracle",
                  "host": "oraprod01.example.com",
                  "serviceName": "ORCLPDB1",
                  "credentialsVariablePrefix": "ORAPROD01",
                  "objectTypes": ["Tables"]
                }
              ]
            }
            """);

        SyncSqlConfig config = await SyncSqlConfigLoader.LoadAsync(path);

        Assert.Equal(2, config.Servers.Count);
        Assert.Equal(DatabaseEngine.MsSql, config.Servers[0].Type);
        Assert.Equal(DatabaseEngine.Oracle, config.Servers[1].Type);
        Assert.Equal(1433, config.Servers[0].EffectivePort);
        Assert.Equal(1521, config.Servers[1].EffectivePort); // no "port" given - Oracle default

        EffectiveFilters filters = EffectiveFilters.Resolve(config.Defaults, config.Servers[0]);
        Assert.False(filters.Databases.IsAllowed("tempdb"));
        Assert.Equal(["Tables", "Views"], filters.ObjectTypes);
    }

    private static async Task<string> WriteTempConfigAsync(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"syncsql-config-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
