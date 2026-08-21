using System.Text.Json;

namespace SyncSql.Core.Configuration;

/// <summary>Thrown when config/servers.json is missing, malformed, or fails validation. Always carries a message meant to be shown directly to the user.</summary>
public sealed class ConfigValidationException(string message) : Exception(message);

/// <summary>Loads and validates a config/servers.json file - a direct port of SyncSql.Common.psm1's Import-SyncSqlConfig.</summary>
public static class SyncSqlConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<SyncSqlConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new ConfigValidationException($"Config file not found: {path}");
        }

        await using FileStream stream = File.OpenRead(path);
        SyncSqlConfig config;
        try
        {
            config = await JsonSerializer.DeserializeAsync<SyncSqlConfig>(stream, SerializerOptions, cancellationToken)
                ?? throw new ConfigValidationException($"Config file '{path}' is empty or 'null'.");
        }
        catch (JsonException ex)
        {
            throw new ConfigValidationException($"Config file '{path}' could not be parsed: {ex.Message}");
        }

        Validate(config, path);
        return config;
    }

    private static void Validate(SyncSqlConfig config, string path)
    {
        if (config.Servers.Count == 0)
        {
            throw new ConfigValidationException($"Config file '{path}' does not define any servers.");
        }

        foreach (ServerConfig server in config.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                throw new ConfigValidationException($"Config file '{path}' has a server entry missing required key 'name'.");
            }
            if (string.IsNullOrWhiteSpace(server.Host))
            {
                throw new ConfigValidationException($"Config file '{path}' has server '{server.Name}' missing required key 'host'.");
            }
            if (string.IsNullOrWhiteSpace(server.CredentialsVariablePrefix))
            {
                throw new ConfigValidationException($"Config file '{path}' has server '{server.Name}' missing required key 'credentialsVariablePrefix'.");
            }
        }
    }
}
