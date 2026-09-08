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

        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ServerConfig server in config.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                throw new ConfigValidationException($"Config file '{path}' has a server entry missing required key 'name'.");
            }
            if (!seenNames.Add(server.Name))
            {
                throw new ConfigValidationException($"Config file '{path}' defines server '{server.Name}' more than once - server names must be unique (they become the top-level output path segment).");
            }
            if (string.IsNullOrWhiteSpace(server.Host))
            {
                throw new ConfigValidationException($"Config file '{path}' has server '{server.Name}' missing required key 'host'.");
            }
            if (string.IsNullOrWhiteSpace(server.CredentialsVariablePrefix))
            {
                throw new ConfigValidationException($"Config file '{path}' has server '{server.Name}' missing required key 'credentialsVariablePrefix'.");
            }
            if (server.Type == Domain.DatabaseEngine.Oracle && string.IsNullOrWhiteSpace(server.ServiceName))
            {
                throw new ConfigValidationException($"Config file '{path}' has Oracle server '{server.Name}' missing required key 'serviceName'.");
            }

            ValidateFilterPatterns(server.Databases, path, $"server '{server.Name}' databases");
            ValidateHostNameSuffix(server.HostNameSuffix, path, server.Name);
            ValidateFilterPatterns(server.Schemas, path, $"server '{server.Name}' schemas");
            ValidateFilterPatterns(server.ObjectNames, path, $"server '{server.Name}' objectNames");
        }

        ValidateFilterPatterns(config.Defaults?.Databases, path, "defaults.databases");
        ValidateFilterPatterns(config.Defaults?.Schemas, path, "defaults.schemas");
        ValidateFilterPatterns(config.Defaults?.ObjectNames, path, "defaults.objectNames");
        ValidateFilterPatterns(config.ServerSelection, path, "serverSelection");
        ValidateFilterPatterns(config.Discovery.LinkedServers.LinkNames, path, "discovery.linkedServers.linkNames");

        if (config.Discovery.LinkedServers.MaxDepth < 0)
        {
            throw new ConfigValidationException($"Config file '{path}' has a negative discovery.linkedServers.maxDepth ({config.Discovery.LinkedServers.MaxDepth}) - use 0 to disable following linked servers.");
        }
    }

    private static void ValidateHostNameSuffix(string? suffix, string path, string serverName)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return;
        }

        string domain = suffix.Trim();
        if (domain.StartsWith('.'))
        {
            domain = domain[1..];
        }

        if (domain.Length > 253 || domain.Split('.').Any(label =>
            label.Length is < 1 or > 63
            || !char.IsAsciiLetterOrDigit(label[0])
            || !char.IsAsciiLetterOrDigit(label[^1])
            || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            throw new ConfigValidationException($"Config file '{path}' has server '{serverName}' with an invalid hostNameSuffix - use a DNS suffix such as 'example.com' or '.example.com'.");
        }
    }

    /// <summary>Every include/exclude entry is a regex evaluated at extraction time - compile each here so a typo fails validate-config instead of mid-extraction.</summary>
    private static void ValidateFilterPatterns(NameFilter? filter, string path, string location)
    {
        if (filter is null)
        {
            return;
        }

        foreach (string pattern in filter.Include.Concat(filter.Exclude))
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                throw new ConfigValidationException($"Config file '{path}' has an invalid regex '{pattern}' in {location}: {ex.Message}");
            }
        }
    }
}
