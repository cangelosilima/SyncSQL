using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Credentials;

/// <summary>
/// Reads "&lt;prefix&gt;_DB_USER"/"&lt;prefix&gt;_DB_PASSWORD" from the process environment, matching
/// config/servers.json's credentialsVariablePrefix convention. Kept as the lowest-precedence layer of
/// <see cref="LayeredCredentialProvider"/> so a host that already exports these (a CI runner, a
/// systemd unit, a developer's shell profile) keeps working without passing any credential parameter.
/// </summary>
public sealed class EnvironmentCredentialProvider : ICredentialProvider
{
    private readonly Func<string, string?> _readVariable;

    public EnvironmentCredentialProvider()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>Seam for tests: reads variables from <paramref name="readVariable"/> instead of the real process environment.</summary>
    public EnvironmentCredentialProvider(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        _readVariable = readVariable;
    }

    public static string UserVariableName(string credentialsVariablePrefix) => $"{credentialsVariablePrefix}_DB_USER";

    public static string PasswordVariableName(string credentialsVariablePrefix) => $"{credentialsVariablePrefix}_DB_PASSWORD";

    public PartialCredentials Read(string credentialsVariablePrefix) => new(
        _readVariable(UserVariableName(credentialsVariablePrefix)),
        _readVariable(PasswordVariableName(credentialsVariablePrefix)));

    public string Describe(string credentialsVariablePrefix) =>
        $"the '{UserVariableName(credentialsVariablePrefix)}'/'{PasswordVariableName(credentialsVariablePrefix)}' environment variables";
}
