using System.Globalization;
using System.Net;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Configuration;

/// <summary>Connection identity carried with exports so link aliases do not become physical servers.</summary>
public sealed record ServerIdentity
{
    public required DatabaseEngine Engine { get; init; }
    public required string Endpoint { get; init; }
    public IReadOnlyList<string> Addresses { get; init; } = [];
    public string? HostNameSuffix { get; init; }

    public static ServerIdentity FromConfig(ServerConfig server)
    {
        string endpoint = server.Type == DatabaseEngine.MsSql
            ? NormalizeSqlAddress(server.Host, server.Port)
            : $"{server.Host.Trim().ToUpperInvariant()}:{server.EffectivePort.ToString(CultureInfo.InvariantCulture)}/{server.ServiceName}";
        return new ServerIdentity
        {
            Engine = server.Type,
            Endpoint = endpoint,
            HostNameSuffix = server.HostNameSuffix,
            Addresses = [.. server.Aliases.Select(alias => NormalizeAddress(server.Type, alias))
                .Append(endpoint).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
        };
    }

    public bool Matches(string address, string? suffix = null) =>
        Addresses.Contains(NormalizeAddress(Engine, address), StringComparer.OrdinalIgnoreCase)
        || (suffix is not null && Addresses.Contains(NormalizeAddress(Engine, address, suffix), StringComparer.OrdinalIgnoreCase));

    public static string NormalizeAddress(DatabaseEngine engine, string address, string? suffix = null) =>
        engine == DatabaseEngine.MsSql ? NormalizeSqlAddress(address, suffix: suffix) : address.Trim().ToUpperInvariant();

    /// <summary>Normalize spelling only. Keep instance and port; never infer DNS/IP equivalence.</summary>
    public static string NormalizeSqlAddress(string address, int? port = null, string? suffix = null)
    {
        string value = address.Trim();
        if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }
        string[] parts = value.Split(',', 2);
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int explicitPort)
                || explicitPort is < 1 or > 65535)
            {
                // Malformed addresses must not accidentally match a valid default-port endpoint.
                return value.ToUpperInvariant();
            }
            port = explicitPort;
        }
        string[] hostParts = parts[0].Trim().Split('\\', 2);
        string host = hostParts[0] == "." ? "." : hostParts[0].TrimEnd('.');
        if (!string.IsNullOrWhiteSpace(suffix) && !host.Contains('.', StringComparison.Ordinal)
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !IPAddress.TryParse(host, out _)
            && host.Length > 0 && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            host += "." + suffix.Trim().Trim('.');
        }
        string instance = hostParts.Length == 2 ? "\\" + hostParts[1] : "";
        int? effectivePort = port ?? (instance.Length == 0 ? 1433 : null);
        return (host + instance + (effectivePort is { } p ? "," + p.ToString(CultureInfo.InvariantCulture) : "")).ToUpperInvariant();
    }
}
