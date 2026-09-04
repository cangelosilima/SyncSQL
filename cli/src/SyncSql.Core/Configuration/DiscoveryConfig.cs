using System.Text.Json.Serialization;

namespace SyncSql.Core.Configuration;

/// <summary>
/// config.discovery.linkedServers - whether a sync run follows the linked servers it finds and extracts
/// what's on the other side, instead of stopping at the servers config lists by hand.
///
/// Off by default: following a link means opening a connection to a host nobody wrote down, so it has to
/// be asked for. When it is on, the credentials already in hand are the ones reused - which is exactly
/// why <see cref="RequireMatchingLogin"/> defaults to true: a link that maps to some other remote login
/// says, in the catalog itself, that this username is not the one that works there.
/// </summary>
public sealed record LinkedServerDiscoveryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>How many links deep to follow: 1 extracts the servers the configured ones link to, 2 also the ones *those* link to, and so on. Depth 0 disables follow-up.</summary>
    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; init; } = 1;

    /// <summary>Only follow a link whose remote login is the username the parent server is already using (or that passes the local login through) - the "same username" rule that keeps a follow-up from failing to authenticate.</summary>
    [JsonPropertyName("requireMatchingLogin")]
    public bool RequireMatchingLogin { get; init; } = true;

    /// <summary>When a link pins a database (sp_addlinkedserver's @catalog), extract only that database on the far side rather than everything the parent's database filter would allow.</summary>
    [JsonPropertyName("restrictToLinkedCatalog")]
    public bool RestrictToLinkedCatalog { get; init; } = true;

    /// <summary>Which link names to follow. Same regex include/exclude semantics as every other filter; empty include means all of them.</summary>
    [JsonPropertyName("linkNames")]
    public NameFilter LinkNames { get; init; } = new();
}

/// <summary>config.discovery - what a sync run is allowed to find on its own, beyond the servers config lists.</summary>
public sealed record DiscoveryConfig
{
    [JsonPropertyName("linkedServers")]
    public LinkedServerDiscoveryConfig LinkedServers { get; init; } = new();
}
