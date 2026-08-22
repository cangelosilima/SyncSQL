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
    public async Task LoadAsync_UnknownEngineType_Throws()
    {
        string path = await WriteTempConfigAsync("""
            {"servers":[{"name":"X","type":"postgres","host":"h","credentialsVariablePrefix":"X"}]}
            """);

        await Assert.ThrowsAsync<ConfigValidationException>(() => SyncSqlConfigLoader.LoadAsync(path));
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
