namespace SyncSql.Core.Configuration;

/// <summary>Unifies declared endpoint aliases, while keeping competing alias claims ambiguous.</summary>
public sealed class ServerIdentityRegistry
{
    private readonly Dictionary<string, string> _parents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ServerIdentity[] _identities;

    public ServerIdentityRegistry(IEnumerable<ServerIdentity> identities)
    {
        ServerIdentity[] all = [.. identities];
        _identities = all;
        foreach (ServerIdentity identity in all)
        {
            _parents.TryAdd(Key(identity), Key(identity));
        }
        foreach (IGrouping<string, ServerIdentity> primary in all.GroupBy(Key, StringComparer.OrdinalIgnoreCase))
        {
            ServerIdentity target = primary.First();
            string[] owners = [.. all.Where(identity => identity.Engine == target.Engine
                    && !string.Equals(Key(identity), primary.Key, StringComparison.OrdinalIgnoreCase)
                    && identity.Addresses.Contains(target.Endpoint, StringComparer.OrdinalIgnoreCase))
                .Select(Key).Distinct(StringComparer.OrdinalIgnoreCase)];
            // An explicit alias that names another registered endpoint asserts equivalence.
            // Competing claims provide no unique mapping unless the endpoint itself explicitly
            // declares the owners as aliases too (a reciprocal, multi-address registration).
            IEnumerable<string> confirmedOwners = owners.Length == 1 ? owners : owners.Where(owner =>
                primary.Any(identity => identity.Addresses.Contains(
                    all.First(candidate => string.Equals(Key(candidate), owner, StringComparison.OrdinalIgnoreCase)).Endpoint,
                    StringComparer.OrdinalIgnoreCase)));
            foreach (string owner in confirmedOwners)
            {
                string left = Find(primary.Key);
                string right = Find(owner);
                if (StringComparer.OrdinalIgnoreCase.Compare(left, right) < 0)
                {
                    _parents[right] = left;
                }
                else
                {
                    _parents[left] = right;
                }
            }
        }
    }

    public string CanonicalKey(ServerIdentity identity) => Find(Key(identity));

    /// <summary>Persist the whole known alias group even if some configured entries were not extracted.</summary>
    public ServerIdentity Describe(ServerIdentity identity)
    {
        string key = CanonicalKey(identity);
        return identity with
        {
            Endpoint = _identities.First(candidate => string.Equals(Key(candidate), key, StringComparison.OrdinalIgnoreCase)).Endpoint,
            Addresses = [.. _identities.Where(candidate => string.Equals(CanonicalKey(candidate), key, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Addresses).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
        };
    }

    private static string Key(ServerIdentity identity) => $"{identity.Engine}|{identity.Endpoint}";

    private string Find(string key)
    {
        while (!string.Equals(_parents[key], key, StringComparison.OrdinalIgnoreCase))
        {
            key = _parents[key];
        }
        return key;
    }
}
