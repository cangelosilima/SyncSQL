using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Configuration;

/// <summary>One server a sync run reached by following a linked server, and the link it came from.</summary>
/// <param name="Server">The synthesized server entry, ready to hand to an extractor exactly like a configured one.</param>
/// <param name="DiscoveredFrom">The configured (or previously discovered) server whose catalog declared the link.</param>
/// <param name="LinkName">The link's name on that server.</param>
/// <param name="Catalog">The database the link pinned, when it pinned one and the plan honoured it.</param>
public sealed record LinkedServerFollowUp(ServerConfig Server, string DiscoveredFrom, string LinkName, string? Catalog);

/// <summary>One link the plan deliberately didn't follow, with the reason, so a run can say why out loud.</summary>
public sealed record SkippedLinkedServer(string LinkName, string Reason);

/// <summary>Everything one server's linked servers turned into: what to extract next, and what was left alone.</summary>
public sealed record LinkedServerFollowUpPlan
{
    public required IReadOnlyList<LinkedServerFollowUp> FollowUps { get; init; }

    public required IReadOnlyList<SkippedLinkedServer> Skipped { get; init; }

    public static LinkedServerFollowUpPlan Empty { get; } = new() { FollowUps = [], Skipped = [] };
}

/// <summary>
/// Turns the linked servers an extraction found into the next round of servers to extract - the
/// "follow the links, with the same username and database" half of looking past one server's own
/// databases.
///
/// The rules are deliberately conservative, because each follow-up opens a connection to a host nobody
/// listed by hand:
/// <list type="bullet">
/// <item>Only SQL Server links. A link to Oracle, Excel or an ODBC DSN isn't something an MSSQL extractor can read.</item>
/// <item>Only links whose remote login is the username already in hand (or that pass the local login through), when
/// <see cref="LinkedServerDiscoveryConfig.RequireMatchingLogin"/> is on - the credentials get reused as-is, and a link
/// mapped to a different login is the catalog saying those credentials aren't the right ones there.</item>
/// <item>The same database the link pins, when it pins one and <see cref="LinkedServerDiscoveryConfig.RestrictToLinkedCatalog"/>
/// is on - a link that names a catalog is pointing at one database, not at the whole instance.</item>
/// <item>Nothing already covered: a server config already lists (by name or by host), or another link on this same
/// server already reached.</item>
/// </list>
/// Everything else about the follow-up server is inherited from its parent - engine, port, TLS settings,
/// filters, and above all <see cref="ServerConfig.CredentialsVariablePrefix"/>, which is what makes the
/// same username and password apply on the other side.
///
/// Pure: it plans, it doesn't connect or touch disk.
/// </summary>
public static class LinkedServerFollowUpPlanner
{
    public static LinkedServerFollowUpPlan Plan(
        ServerConfig parent,
        string parentUsername,
        IReadOnlyList<DiscoveredLinkedServer> discovered,
        LinkedServerDiscoveryConfig config,
        IReadOnlyCollection<ServerConfig> alreadyKnown,
        ObjectFilterSet? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(alreadyKnown);

        if (!config.Enabled || config.MaxDepth <= 0 || discovered.Count == 0)
        {
            return LinkedServerFollowUpPlan.Empty;
        }

        HashSet<string> takenNames = new(alreadyKnown.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        HashSet<string> takenTargets = new(alreadyKnown.Select(TargetKey), StringComparer.OrdinalIgnoreCase);

        List<LinkedServerFollowUp> followUps = [];
        List<SkippedLinkedServer> skipped = [];

        foreach (DiscoveredLinkedServer link in discovered)
        {
            if (!config.LinkNames.IsAllowed(link.Name))
            {
                skipped.Add(new SkippedLinkedServer(link.Name, "excluded by discovery.linkedServers.linkNames"));
                continue;
            }

            if (!IsSqlServer(link))
            {
                skipped.Add(new SkippedLinkedServer(link.Name, $"not a SQL Server link (product '{link.Product}', provider '{link.Provider}')"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(link.DataSource))
            {
                skipped.Add(new SkippedLinkedServer(link.Name, "no data source declared"));
                continue;
            }

            if (config.RequireMatchingLogin && !UsesSameUsername(link, parentUsername))
            {
                skipped.Add(new SkippedLinkedServer(
                    link.Name,
                    $"maps to remote login(s) [{string.Join(", ", link.RemoteLoginNames)}], not '{parentUsername}' - set discovery.linkedServers.requireMatchingLogin to false to follow it anyway"));
                continue;
            }

            (string host, int? port) = ParseDataSource(link.DataSource);
            if (host.Length == 0)
            {
                skipped.Add(new SkippedLinkedServer(link.Name, $"data source '{link.DataSource}' has no host part"));
                continue;
            }

            host = ApplyHostNameSuffix(host, parent.HostNameSuffix);
            string name = UniqueName(link.Name, takenNames);
            string? catalog = config.RestrictToLinkedCatalog && !string.IsNullOrWhiteSpace(link.Catalog) ? link.Catalog : null;

            ServerConfig server = new()
            {
                Name = name,
                ExportPath = [.. parent.ExportPath ?? [parent.Name], "LinkedServers", link.Name],
                Type = DatabaseEngine.MsSql,
                Host = host,
                HostNameSuffix = parent.HostNameSuffix,
                Port = port ?? (parent.Type == DatabaseEngine.MsSql ? parent.Port : null),
                Encrypt = parent.Encrypt,
                TrustServerCertificate = parent.TrustServerCertificate,
                // The same credentials prefix as the parent: same username, same password, on the other
                // side of the link. That reuse is the whole point of the matching-login rule above.
                CredentialsVariablePrefix = parent.CredentialsVariablePrefix,
                Databases = catalog is not null ? OnlyDatabase(catalog) : parent.Databases,
                Schemas = parent.Schemas,
                ObjectNames = parent.ObjectNames,
                // Remote link definitions are not part of the followed database's export.
                // Resolve defaults first so a null override cannot reintroduce LinkedServers.
                ObjectTypes = [.. EffectiveFilters.Resolve(defaults, parent).ObjectTypes
                    .Where(type => !string.Equals(type, "LinkedServers", StringComparison.OrdinalIgnoreCase))],
            };

            string targetKey = TargetKey(server);
            if (!takenTargets.Add(targetKey))
            {
                skipped.Add(new SkippedLinkedServer(link.Name, $"'{host}' is already covered by another server entry"));
                continue;
            }

            takenNames.Add(name);
            followUps.Add(new LinkedServerFollowUp(server, parent.Name, link.Name, catalog));
        }

        return new LinkedServerFollowUpPlan { FollowUps = followUps, Skipped = skipped };
    }

    /// <summary>A link is followable when it says SQL Server, either in sys.servers.product or in the OLE DB provider it uses.</summary>
    private static bool IsSqlServer(DiscoveredLinkedServer link) =>
        string.Equals(link.Product, "SQL Server", StringComparison.OrdinalIgnoreCase)
        || (link.Provider is { Length: > 0 } provider
            && (provider.StartsWith("SQLNCLI", StringComparison.OrdinalIgnoreCase)
                || provider.StartsWith("MSOLEDBSQL", StringComparison.OrdinalIgnoreCase)
                || provider.Equals("SQLOLEDB", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The same username reaches the far side either because the link maps to it by name, or because it passes the local login through unchanged.</summary>
    private static bool UsesSameUsername(DiscoveredLinkedServer link, string parentUsername) =>
        link.UsesLocalLogin
        || link.RemoteLoginNames.Count == 0
        || link.RemoteLoginNames.Any(login => string.Equals(login, parentUsername, StringComparison.OrdinalIgnoreCase));

    /// <summary>"host,1433" / "host\instance" / "host" -> the host (instance kept, it's part of the address) and the explicit port when one was given.</summary>
    private static (string Host, int? Port) ParseDataSource(string dataSource)
    {
        string[] parts = dataSource.Split(',', 2);
        string host = parts[0].Trim();
        int? port = parts.Length == 2
            && int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed > 0
                ? parsed
                : null;
        return (host, port);
    }

    /// <summary>Qualify only a short host, preserving a transport prefix and named instance.</summary>
    private static string ApplyHostNameSuffix(string dataSource, string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return dataSource;
        }

        int hostStart = dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase) ? 4
            : dataSource.StartsWith("np:", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
        int instanceStart = dataSource.IndexOf('\\', hostStart);
        string host = instanceStart < 0 ? dataSource[hostStart..] : dataSource[hostStart..instanceStart];

        // Qualified names, IP literals, local aliases and pipe paths aren't short DNS hosts.
        if (host.Length == 0 || host.Contains('.', StringComparison.Ordinal)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out _)
            || !host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return dataSource;
        }

        return $"{dataSource[..hostStart]}{host}.{suffix.Trim().TrimStart('.')}{(instanceStart < 0 ? "" : dataSource[instanceStart..])}";
    }

    /// <summary>What makes two entries the same target: host (case-insensitively) plus the databases they'd extract, so a link pinned to one catalog doesn't collide with a full-instance entry.</summary>
    private static string TargetKey(ServerConfig server) =>
        $"{server.Host}|{server.Port}|{string.Join(",", server.Databases?.Include ?? [])}";

    /// <summary>Server names become the top-level output path segment, so a discovered one that collides with an existing name is suffixed rather than allowed to overwrite it.</summary>
    private static string UniqueName(string preferred, HashSet<string> taken)
    {
        string candidate = Sanitize(preferred);
        if (!taken.Contains(candidate))
        {
            return candidate;
        }

        for (int suffix = 2; ; suffix++)
        {
            string next = $"{candidate}_{suffix}";
            if (!taken.Contains(next))
            {
                return next;
            }
        }
    }

    /// <summary>A link name can contain anything sys.servers accepts, including characters a path segment can't carry.</summary>
    private static readonly SearchValues<char> UnsafeNameChars = SearchValues.Create([.. Path.GetInvalidFileNameChars(), '\\', '/', ',']);

    private static string Sanitize(string name)
    {
        string result = new string([.. name.Select(c => UnsafeNameChars.Contains(c) ? '_' : c)]).Trim();
        return result.Length == 0 ? "LinkedServer" : result;
    }

    private static NameFilter OnlyDatabase(string database) => new() { Include = [$"^{Regex.Escape(database)}$"] };
}
