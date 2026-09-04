using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Credentials;

/// <summary>
/// Credentials handed over directly, keyed by a server's credentialsVariablePrefix - the CLI's
/// repeatable <c>--db-user PREFIX=value</c>/<c>--db-password PREFIX=value</c> parameters. Highest
/// precedence layer of <see cref="LayeredCredentialProvider"/>, and the reason the CLI no longer needs
/// anything in its environment to run away from a CI job.
/// </summary>
public sealed class ExplicitCredentialProvider : ICredentialProvider
{
    private const string DefaultSource = "the --db-user/--db-password parameters";

    private readonly IReadOnlyDictionary<string, PartialCredentials> _byPrefix;
    private readonly string _source;

    public ExplicitCredentialProvider(IReadOnlyDictionary<string, PartialCredentials> credentialsByPrefix, string source = DefaultSource)
    {
        ArgumentNullException.ThrowIfNull(credentialsByPrefix);
        _byPrefix = credentialsByPrefix;
        _source = source;
    }

    /// <summary>True when nothing at all was supplied, so callers can skip layering this in.</summary>
    public bool IsEmpty => _byPrefix.Count == 0;

    /// <summary>
    /// Parses repeatable <c>PREFIX=value</c> arguments into one provider. Only the first '=' separates,
    /// so a password containing '=' needs no escaping; a prefix given twice for the same half is an error
    /// rather than a silent last-one-wins.
    /// </summary>
    public static ExplicitCredentialProvider FromArguments(
        IEnumerable<string> users,
        IEnumerable<string> passwords,
        string source = DefaultSource)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(passwords);

        Dictionary<string, string> parsedUsers = ParsePairs(users, "--db-user");
        Dictionary<string, string> parsedPasswords = ParsePairs(passwords, "--db-password");

        Dictionary<string, PartialCredentials> byPrefix = new(StringComparer.OrdinalIgnoreCase);
        foreach (string prefix in parsedUsers.Keys.Concat(parsedPasswords.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            parsedUsers.TryGetValue(prefix, out string? user);
            parsedPasswords.TryGetValue(prefix, out string? password);
            byPrefix[prefix] = new PartialCredentials(user, password);
        }

        return new ExplicitCredentialProvider(byPrefix, source);
    }

    public PartialCredentials Read(string credentialsVariablePrefix) =>
        _byPrefix.TryGetValue(credentialsVariablePrefix, out PartialCredentials credentials) ? credentials : PartialCredentials.None;

    public string Describe(string credentialsVariablePrefix) => _source;

    private static Dictionary<string, string> ParsePairs(IEnumerable<string> arguments, string optionName)
    {
        Dictionary<string, string> parsed = new(StringComparer.OrdinalIgnoreCase);

        foreach (string argument in arguments)
        {
            int separator = argument.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new CredentialParseException(
                    $"Invalid {optionName} value '{Redact(argument, optionName)}' - expected PREFIX=value, where PREFIX is a server's credentialsVariablePrefix from config/servers.json.");
            }

            string prefix = argument[..separator].Trim();
            string value = argument[(separator + 1)..];
            if (prefix.Length == 0 || value.Length == 0)
            {
                throw new CredentialParseException(
                    $"Invalid {optionName} value for prefix '{prefix}' - both sides of PREFIX=value must be non-empty.");
            }

            if (!parsed.TryAdd(prefix, value))
            {
                throw new CredentialParseException($"{optionName} was given more than once for prefix '{prefix}'.");
            }
        }

        return parsed;
    }

    /// <summary>Never echo a malformed --db-password back at the user: the whole argument is the secret when the '=' is missing.</summary>
    private static string Redact(string argument, string optionName) =>
        optionName == "--db-password" ? new string('*', Math.Min(argument.Length, 8)) : argument;
}
