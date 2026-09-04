using System.Text.Json;
using System.Text.Json.Serialization;
using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Credentials;

/// <summary>One server's credentials inside a credentials file; either half may be omitted and filled in by a lower-precedence source.</summary>
public sealed record CredentialsFileEntry
{
    [JsonPropertyName("user")]
    public string? User { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }
}

/// <summary>
/// Credentials read from a JSON file keyed by credentialsVariablePrefix:
/// <c>{ "SQLPROD01": { "user": "svc_syncsql", "password": "..." } }</c>. The way to run the CLI outside a
/// CI job without exporting anything into the environment or putting a password in a shell history/process
/// listing - keep the file outside the repository and readable only by the account running <c>syncsql</c>.
/// </summary>
public sealed class CredentialsFileProvider : ICredentialProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IReadOnlyDictionary<string, PartialCredentials> _byPrefix;
    private readonly string _path;

    private CredentialsFileProvider(IReadOnlyDictionary<string, PartialCredentials> byPrefix, string path)
    {
        _byPrefix = byPrefix;
        _path = path;
    }

    public static async Task<CredentialsFileProvider> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new CredentialParseException($"Credentials file not found: {path}");
        }

        Dictionary<string, CredentialsFileEntry?>? entries;
        try
        {
            await using FileStream stream = File.OpenRead(path);
            entries = await JsonSerializer.DeserializeAsync<Dictionary<string, CredentialsFileEntry?>>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new CredentialParseException(
                $"Credentials file '{path}' could not be parsed: {ex.Message} Expected {{ \"PREFIX\": {{ \"user\": \"...\", \"password\": \"...\" }} }}.");
        }

        if (entries is null)
        {
            throw new CredentialParseException($"Credentials file '{path}' is empty or 'null'.");
        }

        Dictionary<string, PartialCredentials> byPrefix = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string prefix, CredentialsFileEntry? entry) in entries)
        {
            byPrefix[prefix] = new PartialCredentials(entry?.User, entry?.Password);
        }

        return new CredentialsFileProvider(byPrefix, path);
    }

    public PartialCredentials Read(string credentialsVariablePrefix) =>
        _byPrefix.TryGetValue(credentialsVariablePrefix, out PartialCredentials credentials) ? credentials : PartialCredentials.None;

    public string Describe(string credentialsVariablePrefix) => $"the credentials file '{_path}'";
}
