using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public sealed class ServerIdentityTests
{
    [Theory]
    [InlineData(" tcp:Sql01.,1433 ", "SQL01,1433")]
    [InlineData("sql01", "SQL01,1433")]
    [InlineData("sql01,1444", "SQL01,1444")]
    [InlineData(@"sql01\Finance", @"SQL01\FINANCE")]
    [InlineData(@"sql01\Finance,1444", @"SQL01\FINANCE,1444")]
    public void Normalization_preserves_instance_and_port(string address, string expected) =>
        Assert.Equal(expected, ServerIdentity.NormalizeSqlAddress(address));

    [Fact]
    public void Only_explicit_aliases_establish_equivalence()
    {
        ServerIdentity identity = ServerIdentity.FromConfig(new ServerConfig
        {
            Name = "Finance",
            Type = DatabaseEngine.MsSql,
            Host = "sql01.example.com",
            CredentialsVariablePrefix = "FINANCE",
            Aliases = ["10.0.0.5", "sql01"],
        });
        Assert.True(identity.Matches("tcp:10.0.0.5,1433"));
        Assert.True(identity.Matches("SQL01"));
        Assert.False(identity.Matches("sql01.other.com"));
        Assert.False(identity.Matches("10.0.0.5,1444"));
        Assert.False(identity.Matches(@"sql01\Other"));
    }

    [Fact]
    public void RegistryUnifiesAnExplicitAliasOfAnotherConfiguredEndpoint()
    {
        ServerConfig config = new() { Name = "SQL", Host = "sql.example.com", Type = DatabaseEngine.MsSql, CredentialsVariablePrefix = "SQL", Aliases = ["10.0.0.5"] };
        ServerIdentity dns = ServerIdentity.FromConfig(config);
        ServerIdentity ip = ServerIdentity.FromConfig(config with { Host = "10.0.0.5", Aliases = [] });
        ServerIdentity otherPort = ServerIdentity.FromConfig(config with { Port = 1444, Aliases = [] });
        foreach (ServerIdentity[] order in new[] { new[] { dns, ip, otherPort }, new[] { otherPort, ip, dns } })
        {
            ServerIdentityRegistry registry = new(order);
            Assert.Equal(registry.CanonicalKey(dns), registry.CanonicalKey(ip));
            Assert.NotEqual(registry.CanonicalKey(dns), registry.CanonicalKey(otherPort));
        }
    }

    [Fact]
    public void RegistryDoesNotUnifyCompetingAliasClaims()
    {
        ServerConfig config = new() { Name = "SQL", Host = "one", Type = DatabaseEngine.MsSql, CredentialsVariablePrefix = "SQL", Aliases = ["shared"] };
        ServerIdentity first = ServerIdentity.FromConfig(config);
        ServerIdentity second = ServerIdentity.FromConfig(config with { Host = "two" });
        ServerIdentity shared = ServerIdentity.FromConfig(config with { Host = "shared", Aliases = [] });
        ServerIdentityRegistry registry = new([first, second, shared]);
        Assert.NotEqual(registry.CanonicalKey(first), registry.CanonicalKey(second));
        Assert.NotEqual(registry.CanonicalKey(first), registry.CanonicalKey(shared));
    }

    [Fact]
    public void RegistryUnifiesReciprocalAliasesAcrossSeveralConfiguredAddresses()
    {
        ServerConfig config = new() { Name = "SQL", Host = "one", Type = DatabaseEngine.MsSql, CredentialsVariablePrefix = "SQL", Aliases = ["one", "two", "three"] };
        ServerIdentity[] identities = [.. new[] { "one", "two", "three" }.Select(host => ServerIdentity.FromConfig(config with { Host = host }))];
        foreach (var order in new[] { identities, identities.Reverse().ToArray() })
        {
            ServerIdentityRegistry registry = new(order);
            Assert.Single(identities.Select(registry.CanonicalKey).Distinct());
            Assert.All(identities, identity => Assert.Equal(3, registry.Describe(identity).Addresses.Count));
        }
    }
}
