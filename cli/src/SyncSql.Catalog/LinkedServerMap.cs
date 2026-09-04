using System.Text.RegularExpressions;
using SyncSql.Core.Domain;

namespace SyncSql.Catalog;

/// <summary>
/// One linked server (MSSQL) / database link (Oracle) as the catalog knows it: the extracted link
/// object itself, what it declares it points at, and - when anything in the catalog answers to that -
/// the catalog server a reference crossing it lands on.
/// </summary>
/// <param name="NodeId">Node id of the extracted LinkedServers/DatabaseLinks object.</param>
/// <param name="Name">The link's name, i.e. what a reference writes before the first dot (T-SQL) or after the "@" (PL/SQL).</param>
/// <param name="OnServer">The catalog server the link is declared on.</param>
/// <param name="DataSource">The host / TNS alias the link points at, as declared.</param>
/// <param name="DefaultDatabase">The database the link itself declares (MSSQL's <c>@catalog</c>), used when a reference through it doesn't name one.</param>
/// <param name="TargetServer">The catalog server this link lands on, or null when no extracted server answers to it - a hop out of the catalog's scope. Equal to <paramref name="OnServer"/> for a loopback link, which is no hop at all.</param>
internal sealed record LinkedServerLink(
    string NodeId,
    string Name,
    string OnServer,
    string? DataSource,
    string? DefaultDatabase,
    string? TargetServer);

/// <summary>
/// Maps a linked-server (MSSQL) or database-link (Oracle) name, as written in one object's DDL, onto the
/// extracted link object and - where possible - a server that's actually in the catalog. This is the
/// piece that lets reference resolution follow a "LNK.OtherDb.dbo.Orders" or "orders@LNK" hop instead of
/// giving up at the server boundary, and the piece that lets each link object list everything referenced
/// through it.
///
/// The link objects are already extracted (LinkedServers / DatabaseLinks), so the mapping is built from
/// their DDL: MSSQL's sp_addlinkedserver call carries the link's <c>@datasrc</c> and <c>@catalog</c>,
/// Oracle's CREATE DATABASE LINK its <c>USING</c> connect string. What a link points at is a host (or a
/// TNS alias); what the catalog is keyed by is the *configured* server name, and those two need not be
/// spelled the same - so a link lands on a catalog server when that server's name equals the link's own
/// name, its data source, or the host part of that data source (port, instance and DNS suffix stripped).
/// A link nothing answers to still resolves to the link itself, just without a target server: a
/// reference through it is out of the catalog's scope, which is a different thing from "the target was
/// dropped".
/// </summary>
internal sealed partial class LinkedServerMap
{
    /// <summary>"&lt;fromServer&gt;::&lt;link name&gt;" -> that link.</summary>
    private readonly Dictionary<string, LinkedServerLink> _linksByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Server -> every catalog server reachable from it in one hop, deduplicated and ordered.</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _reachableByServer = new(StringComparer.OrdinalIgnoreCase);

    public static LinkedServerMap Empty { get; } = new();

    private LinkedServerMap()
    {
    }

    public static LinkedServerMap FromNodes(IEnumerable<CatalogNode> nodes)
    {
        List<CatalogNode> linkNodes = [];
        HashSet<string> catalogServers = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogNode node in nodes)
        {
            catalogServers.Add(node.Server);
            if (node.Type is "LinkedServers" or "DatabaseLinks")
            {
                linkNodes.Add(node);
            }
        }

        LinkedServerMap map = new();
        Dictionary<string, SortedSet<string>> reachable = new(StringComparer.OrdinalIgnoreCase);

        foreach (CatalogNode node in linkNodes)
        {
            string? dataSource = FirstCapture(node.Ddl, MsSqlDataSource()) ?? FirstCapture(node.Ddl, OracleUsing());
            string? catalog = FirstCapture(node.Ddl, MsSqlCatalog());
            string? targetServer = MatchCatalogServer(catalogServers, node.Name, dataSource);

            // A link name is unique per server, so the first entry wins; Oracle qualifies links by owner,
            // but the name is still what the DDL writes after the "@".
            map._linksByName.TryAdd(
                $"{node.Server}::{node.Name}",
                new LinkedServerLink(node.Id, node.Name, node.Server, dataSource, NullIfBlank(catalog), targetServer));

            // A link that comes back to the server it's declared on adds no reachability - following it
            // would only find what the same-server lookup already does. It stays resolvable by name (a
            // reference through it is still a local reference, not one that left the catalog).
            if (targetServer is null || string.Equals(targetServer, node.Server, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!reachable.TryGetValue(node.Server, out SortedSet<string>? targets))
            {
                targets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                reachable[node.Server] = targets;
            }
            targets.Add(targetServer);
        }

        foreach ((string server, SortedSet<string> targets) in reachable)
        {
            map._reachableByServer[server] = [.. targets];
        }

        return map;
    }

    /// <summary>The link "<paramref name="linkName"/>" as written on <paramref name="fromServer"/>, or null when that server declares no such link.</summary>
    public LinkedServerLink? Resolve(string fromServer, string linkName)
    {
        if (_linksByName.TryGetValue($"{fromServer}::{linkName}", out LinkedServerLink? link))
        {
            return link;
        }

        // Oracle writes a link's full "NAME.DOMAIN" (and T-SQL sometimes an FQDN) where the link object
        // itself is named by the bare label - try that before giving up.
        int firstDot = linkName.IndexOf('.', StringComparison.Ordinal);
        return firstDot > 0 && _linksByName.TryGetValue($"{fromServer}::{linkName[..firstDot]}", out LinkedServerLink? shortLink)
            ? shortLink
            : null;
    }

    /// <summary>Every catalog server reachable from <paramref name="fromServer"/> through one link - the set a reference that named no server at all may still be looked up in, as a last resort.</summary>
    public IReadOnlyList<string> ReachableFrom(string fromServer) =>
        _reachableByServer.TryGetValue(fromServer, out IReadOnlyList<string>? targets) ? targets : [];

    /// <summary>The link on <paramref name="fromServer"/> that lands on <paramref name="targetServer"/>, for attributing a hop the lookup took without a named link.</summary>
    public LinkedServerLink? LinkTo(string fromServer, string targetServer)
    {
        foreach (LinkedServerLink link in _linksByName.Values)
        {
            if (string.Equals(link.OnServer, fromServer, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.TargetServer, targetServer, StringComparison.OrdinalIgnoreCase))
            {
                return link;
            }
        }

        return null;
    }

    // sp_addlinkedserver's own arguments, as LinkedServerDdlBuilder writes them.
    [GeneratedRegex(@"@datasrc\s*=\s*N'([^']*)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MsSqlDataSource();

    [GeneratedRegex(@"@catalog\s*=\s*N'([^']*)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MsSqlCatalog();

    // Oracle's DBMS_METADATA CREATE DATABASE LINK ... USING 'tns_alias_or_easy_connect'.
    [GeneratedRegex(@"\bUSING\s+'([^']*)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OracleUsing();

    private static string? FirstCapture(string text, Regex pattern) =>
        pattern.Match(text) is { Success: true } match ? NullIfBlank(match.Groups[1].Value) : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The catalog server a link points at: its own name first (the common case - a linked server is
    /// usually named after the server it reaches), then the data source as written, then that data
    /// source with "host,port" / "host\instance" / a DNS suffix trimmed off.
    /// </summary>
    private static string? MatchCatalogServer(HashSet<string> catalogServers, string linkName, string? dataSource)
    {
        foreach (string candidate in Candidates(linkName, dataSource))
        {
            if (catalogServers.TryGetValue(candidate, out string? actual))
            {
                return actual;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string linkName, string? dataSource)
    {
        yield return linkName;

        foreach (string form in HostForms(linkName))
        {
            yield return form;
        }

        if (dataSource is null)
        {
            yield break;
        }

        yield return dataSource;
        foreach (string form in HostForms(dataSource))
        {
            yield return form;
        }
    }

    /// <summary>"sqlprod01.corp.example.com,1433" -> "sqlprod01.corp.example.com" -> "sqlprod01"; "SQLPROD01\INST" -> "SQLPROD01".</summary>
    private static IEnumerable<string> HostForms(string value)
    {
        string host = value.Split(',', 2)[0].Split('\\', 2)[0].Trim();
        if (host.Length > 0 && !string.Equals(host, value, StringComparison.Ordinal))
        {
            yield return host;
        }

        int firstDot = host.IndexOf('.', StringComparison.Ordinal);
        if (firstDot > 0)
        {
            yield return host[..firstDot];
        }
    }
}
