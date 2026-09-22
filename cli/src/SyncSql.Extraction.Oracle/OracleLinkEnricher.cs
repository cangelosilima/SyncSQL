using System.Text.RegularExpressions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Extraction.Oracle;

/// <summary>Resolves copied server-side network configuration without opening a remote connection.</summary>
internal sealed class OracleLinkEnricher(ServerConfig server)
{
    private static readonly string[] EndpointKeys = ["HOST", "PORT", "SID", "SERVICE_NAME"];
    private readonly Dictionary<string, string?> _files = new(StringComparer.Ordinal);

    public async Task<LinkMetadata> EnrichAsync(string owner, string name, string? username, string? connectIdentifier, CancellationToken token)
    {
        List<LinkEvidence> evidence = [];
        List<string> diagnostics = [];
        LinkMetadata metadata = new()
        {
            ConnectIdentifier = connectIdentifier,
            Logins = username is { Length: > 0 } ? [new(username)] : [],
            Evidence = evidence,
            Diagnostics = diagnostics,
        };
        Add("connectIdentifier", connectIdentifier, "Oracle:*_DB_LINKS.HOST");
        Add("remoteUser", username, "Oracle:*_DB_LINKS.USERNAME");

        if (!string.IsNullOrWhiteSpace(connectIdentifier))
        {
            string? descriptor = connectIdentifier.TrimStart().StartsWith('(') ? connectIdentifier : null;
            string descriptorSource = "Oracle:*_DB_LINKS.HOST";
            if (descriptor is null && server.OracleNetwork?.TnsNamesFile is { } tnsFile)
            {
                string? text = await ReadAsync(tnsFile, token);
                if (text is null) { diagnostics.Add($"Oracle Net configuration unavailable: {Path.GetFileName(tnsFile)}"); }
                else
                {
                    descriptor = FindDescriptor(text, connectIdentifier);
                    descriptorSource = "tnsnames:" + Path.GetFileName(tnsFile);
                    if (descriptor is null) { diagnostics.Add("Connect identifier not uniquely resolved in the supplied Oracle Net configuration."); }
                }
            }
            if (descriptor is not null)
            {
                if (EndpointKeys.Any(key => Leaves(descriptor, key).Length > 1))
                {
                    diagnostics.Add("Oracle Net descriptor names multiple endpoints; supply an explicit destination mapping.");
                    return LinkTargetEnrichment.Apply(server, name, owner, metadata);
                }
                string? host = Leaf(descriptor, "HOST");
                string? sid = Leaf(descriptor, "SID");
                string? service = Leaf(descriptor, "SERVICE_NAME");
                string? port = Leaf(descriptor, "PORT");
                bool gateway = string.Equals(Leaf(descriptor, "HS"), "OK", StringComparison.OrdinalIgnoreCase);
                if (gateway)
                {
                    metadata = metadata with { GatewayHost = host, GatewaySid = sid };
                    Add("gatewayHost", host, descriptorSource);
                    Add("gatewaySid", sid, descriptorSource);
                    GatewayConfig[] configurations = [.. (server.OracleNetwork?.Gateways ?? []).Where(g =>
                        sid is not null && g.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase)
                        && (g.Host is null || g.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))];
                    if (host is null || configurations.Length != 1)
                    {
                        diagnostics.Add("Gateway destination requires one matching gateway configuration (host and SID).");
                    }
                    else
                    {
                        GatewayConfig config = configurations[0];
                        string? init = await ReadAsync(config.InitFile, token);
                        string? connect = init is null ? null : Setting(init, "HS_FDS_CONNECT_INFO");
                        if (connect is null) { diagnostics.Add("Gateway HS_FDS_CONNECT_INFO is unavailable or ambiguous."); }
                        else
                        {
                            string source = "gateway:" + Path.GetFileName(config.InitFile);
                            (string? endpoint, string? database) = (null, null);
                            if (config.OdbcIniFile is { } odbcFile)
                            {
                                string? odbc = await ReadAsync(odbcFile, token);
                                string? section = odbc is null ? null : IniSection(odbc, connect);
                                if (section is not null)
                                {
                                    endpoint = Setting(section, "Server") ?? Setting(section, "Servername");
                                    string? odbcPort = Setting(section, "Port");
                                    if (endpoint is not null && odbcPort is not null && !endpoint.Contains(',')) { endpoint += "," + odbcPort; }
                                    database = Setting(section, "Database");
                                    source = "odbc:" + Path.GetFileName(odbcFile) + "/" + connect;
                                }
                                else { diagnostics.Add("Gateway ODBC DSN is unavailable or ambiguous in the supplied file."); }
                            }
                            else if (config.TargetEngine == DatabaseEngine.MsSql)
                            {
                                (endpoint, database) = ParseSqlServerDestination(connect);
                            }
                            if (endpoint is not null)
                            {
                                metadata = metadata with { TargetEngine = config.TargetEngine, DataSource = endpoint, Database = database };
                                Add("dataSource", endpoint, source);
                                Add("database", database, source);
                                Add("targetEngine", config.TargetEngine.ToConfigString(), source);
                            }
                            else { diagnostics.Add("Gateway destination could not be resolved; supply a linkTargets mapping."); }
                        }
                    }
                }
                else if (host is not null && service is not null)
                {
                    metadata = metadata with { TargetEngine = DatabaseEngine.Oracle, DataSource = $"{host}:{port ?? "1521"}/{service}", Database = service };
                    Add("dataSource", metadata.DataSource, descriptorSource);
                    Add("database", service, descriptorSource);
                }
                else { diagnostics.Add("Oracle Net descriptor does not identify one service endpoint."); }
            }
        }
        return LinkTargetEnrichment.Apply(server, name, owner, metadata);

        void Add(string field, string? value, string source)
        {
            if (!string.IsNullOrWhiteSpace(value)) { evidence.Add(new(field, value, source)); }
        }
    }

    private async Task<string?> ReadAsync(string path, CancellationToken token)
    {
        if (_files.TryGetValue(path, out string? content)) { return content; }
        try { content = await File.ReadAllTextAsync(path, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { content = null; }
        _files[path] = content;
        return content;
    }

    // Only selected, non-secret settings are returned. Never export the initialization/ODBC files.
    private static string? Setting(string text, string key)
    {
        string[] values = [.. text.Split('\n').Select(line => RemoveComment(line).Trim()).Where(line => !line.StartsWith(';'))
            .Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim().Trim('"', '\'')).Distinct(StringComparer.Ordinal)];
        return values.Length == 1 && values[0].Length > 0 ? values[0] : null;
    }

    private static string? IniSection(string text, string name)
    {
        string[] sections = Regex.Split(text, @"(?m)^\s*\[([^\]\r\n]+)\]\s*\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        List<string> matches = [];
        for (int i = 1; i + 1 < sections.Length; i += 2)
        {
            if (sections[i].Equals(name, StringComparison.OrdinalIgnoreCase)) { matches.Add(sections[i + 1]); }
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static (string? Endpoint, string? Database) ParseSqlServerDestination(string connect)
    {
        string[] parts = connect.Split('/');
        if (parts.Length > 3 || string.IsNullOrWhiteSpace(parts[0]) || connect.Contains('=') || connect.Contains(';')) { return (null, null); }
        string host = parts[0].Trim();
        // Dedicated gateway syntax: host[:port]/instance/database; bracketed IPv6 is retained.
        Match address = Regex.Match(host, @"^(\[[^\]]+\]|[^:]+)(?::([0-9]+))?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!address.Success) { return (null, null); }
        string? port = address.Groups[2].Success ? address.Groups[2].Value : null;
        if (port is not null && (!int.TryParse(port, out int number) || number is < 1 or > 65535)) { return (null, null); }
        string? instance = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        if (instance is not null && port is not null) { return (null, null); }
        return (address.Groups[1].Value + (instance is null ? "" : "\\" + instance) + (port is null ? "" : "," + port),
            parts.Length == 3 && parts[2].Length > 0 ? parts[2] : null);
    }

    private static string? Leaf(string descriptor, string key)
    {
        string[] values = Leaves(descriptor, key);
        return values.Length == 1 ? values[0] : null;
    }

    private static string[] Leaves(string descriptor, string key) => [.. Regex.Matches(descriptor, @"\(\s*" + key + @"\s*=\s*([^()]+?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        .Select(match => match.Groups[1].Value.Trim().Trim('"', '\'')).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string? FindDescriptor(string text, string alias)
    {
        text = string.Join('\n', text.Split('\n').Select(RemoveComment));
        List<string> matches = [];
        foreach (Match match in Regex.Matches(text, @"(?m)^\s*([\w.,\- ]+)\s*=\s*(?=\()", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            if (!match.Groups[1].Value.Split(',').Any(value => value.Trim().Equals(alias, StringComparison.OrdinalIgnoreCase))) { continue; }
            int start = match.Index + match.Length;
            int depth = 0;
            char quote = '\0';
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == quote) { quote = '\0'; } continue; }
                if (c is '\'' or '"') { quote = c; continue; }
                if (c == '(') { depth++; }
                if (c == ')' && --depth == 0) { matches.Add(text[start..(i + 1)]); break; }
            }
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string RemoveComment(string line)
    {
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0') { if (c == quote) { quote = '\0'; } }
            else if (c is '\'' or '"') { quote = c; }
            else if (c == '#') { return line[..i]; }
        }
        return line;
    }
}
