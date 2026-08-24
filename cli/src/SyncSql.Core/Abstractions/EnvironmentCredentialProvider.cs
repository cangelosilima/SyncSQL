namespace SyncSql.Core.Abstractions;

/// <summary>
/// The only production <see cref="ICredentialProvider"/>: reads "&lt;prefix&gt;_DB_USER"/"&lt;prefix&gt;_DB_PASSWORD"
/// from the process environment, matching config/servers.json's credentialsVariablePrefix convention.
/// Pure enough (just Environment.GetEnvironmentVariable) to live in Core rather than needing its own
/// infrastructure project.
/// </summary>
public sealed class EnvironmentCredentialProvider : ICredentialProvider
{
    public DatabaseCredentials Resolve(string credentialsVariablePrefix)
    {
        string userVar = $"{credentialsVariablePrefix}_DB_USER";
        string passwordVar = $"{credentialsVariablePrefix}_DB_PASSWORD";

        string? username = Environment.GetEnvironmentVariable(userVar);
        string? password = Environment.GetEnvironmentVariable(passwordVar);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                $"Missing credentials: both '{userVar}' and '{passwordVar}' environment variables must be set.");
        }

        return new DatabaseCredentials(username, password);
    }
}
