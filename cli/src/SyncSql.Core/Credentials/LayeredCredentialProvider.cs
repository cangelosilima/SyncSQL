using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Credentials;

/// <summary>
/// Stacks credential sources in precedence order - CLI parameters, then a credentials file, then the
/// process environment - resolving each half independently, so a username passed as a parameter can be
/// completed by a password only the environment has. When nothing supplies a half, the error names every
/// source that was tried instead of only the last one.
/// </summary>
public sealed class LayeredCredentialProvider : ICredentialProvider
{
    private readonly IReadOnlyList<ICredentialProvider> _layers;

    public LayeredCredentialProvider(params ICredentialProvider[] layers)
        : this((IReadOnlyList<ICredentialProvider>)layers)
    {
    }

    public LayeredCredentialProvider(IReadOnlyList<ICredentialProvider> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("At least one credential source is required.", nameof(layers));
        }
        _layers = layers;
    }

    public PartialCredentials Read(string credentialsVariablePrefix)
    {
        string? username = null;
        string? password = null;

        foreach (ICredentialProvider layer in _layers)
        {
            PartialCredentials credentials = layer.Read(credentialsVariablePrefix);
            username = Coalesce(username, credentials.Username);
            password = Coalesce(password, credentials.Password);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                break;
            }
        }

        return new PartialCredentials(username, password);
    }

    public string Describe(string credentialsVariablePrefix) =>
        string.Join(", then ", _layers.Select(layer => layer.Describe(credentialsVariablePrefix)));

    private static string? Coalesce(string? current, string? candidate) =>
        string.IsNullOrEmpty(current) ? (string.IsNullOrEmpty(candidate) ? current : candidate) : current;
}
