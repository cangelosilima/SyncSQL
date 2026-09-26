using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public sealed class LinkedServerDiscoveryContractTests
{
    private static readonly ServerConfig Parent = new()
    {
        Name = "Source",
        Host = "source",
        Type = DatabaseEngine.MsSql,
        CredentialsVariablePrefix = "SQL",
        ObjectTypes = ["Tables"],
    };
    private static DiscoveredLinkedServer Link(string name, string endpoint) => new()
    {
        Name = name,
        DataSource = endpoint,
        Product = "SQL Server",
        UsesLocalLogin = true,
    };

    [Fact]
    public void ZeroDepthPreventsDiscoveryEvenWhenEnabled()
    {
        var plan = LinkedServerFollowUpPlanner.Plan(Parent, "user", [Link("remote", "remote")], new() { Enabled = true, MaxDepth = 0 }, [Parent]);
        Assert.Empty(plan.FollowUps);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void AnyMatchingRemoteLoginPermitsFollowingTheLink()
    {
        var link = Link("remote", "remote") with { UsesLocalLogin = false, RemoteLoginNames = ["other", "USER"] };
        var plan = LinkedServerFollowUpPlanner.Plan(Parent, "user", [link], new() { Enabled = true }, [Parent]);
        Assert.Single(plan.FollowUps);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void RegisteredEndpointKeepsItsPortAndSuffixWhenDiscoveredThroughAnAlias()
    {
        ServerConfig registered = Parent with { Name = "Registered", Host = "physical.example", Port = 1444, HostNameSuffix = "target.example", Aliases = ["alias,1433"] };
        var plan = LinkedServerFollowUpPlanner.Plan(Parent, "user", [Link("remote", "alias,1433")],
            new() { Enabled = true }, [Parent, registered], coveredServers: [Parent]);
        ServerConfig target = Assert.Single(plan.FollowUps).Server;
        Assert.Equal("physical.example", target.Host);
        Assert.Equal(1444, target.Port);
        Assert.Equal("target.example", target.HostNameSuffix);
    }

    [Fact]
    public void NewTargetsWithCollidingLinkNamesReceiveDistinctServerNames()
    {
        var plan = LinkedServerFollowUpPlanner.Plan(Parent, "user", [Link("remote", "one"), Link("REMOTE", "two")],
            new() { Enabled = true }, [Parent]);
        Assert.Equal(2, plan.FollowUps.Count);
        Assert.Equal(2, plan.FollowUps.Select(followUp => followUp.Server.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
