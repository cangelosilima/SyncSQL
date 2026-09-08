using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public class LinkedServerFollowUpPlannerTests
{
    [Fact]
    public void Plan_ExportPath_PreservesRootAndLinkAncestry()
    {
        ServerConfig remote = Assert.Single(LinkedServerFollowUpPlanner.Plan(Parent, "svc_syncsql", [Link("REMOTE")], Enabled, [Parent]).FollowUps).Server;
        Assert.Equal(["SQLPROD01", "LinkedServers", "REMOTE"], remote.ExportPath);
        ServerConfig nested = Assert.Single(LinkedServerFollowUpPlanner.Plan(remote, "svc_syncsql", [Link("NEXT", dataSource: "next.example.com")], Enabled, [Parent, remote]).FollowUps).Server;
        Assert.Equal(["SQLPROD01", "LinkedServers", "REMOTE", "LinkedServers", "NEXT"], nested.ExportPath);
        Assert.Null(Parent.ExportPath);
    }
    private static readonly ServerConfig Parent = new()
    {
        Name = "SQLPROD01",
        Type = DatabaseEngine.MsSql,
        Host = "sqlprod01.example.com",
        Port = 1433,
        Encrypt = true,
        TrustServerCertificate = true,
        CredentialsVariablePrefix = "SQLPROD01",
        Databases = new NameFilter { Include = [".*"], Exclude = ["^tempdb$"] },
        Schemas = new NameFilter { Include = ["^dbo$"] },
        ObjectTypes = ["Tables", "Views"],
    };

    private static readonly LinkedServerDiscoveryConfig Enabled = new() { Enabled = true };

    private static DiscoveredLinkedServer Link(
        string name,
        string dataSource = "sqlprod02.example.com",
        string? catalog = null,
        string product = "SQL Server",
        string? provider = "SQLNCLI",
        IReadOnlyList<string>? remoteLogins = null,
        bool usesLocalLogin = true) => new()
        {
            Name = name,
            Product = product,
            Provider = provider,
            DataSource = dataSource,
            Catalog = catalog,
            RemoteLoginNames = remoteLogins ?? [],
            UsesLocalLogin = usesLocalLogin,
        };

    [Fact]
    public void Plan_Disabled_FollowsNothing()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SQLPROD02")], new LinkedServerDiscoveryConfig(), [Parent]);

        Assert.Empty(plan.FollowUps);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Plan_ReusesTheParentsCredentialsPrefixAndFilters()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SQLPROD02")], Enabled, [Parent]);

        LinkedServerFollowUp followUp = Assert.Single(plan.FollowUps);
        Assert.Equal("SQLPROD02", followUp.Server.Name);
        Assert.Equal("sqlprod02.example.com", followUp.Server.Host);
        Assert.Equal(Parent.CredentialsVariablePrefix, followUp.Server.CredentialsVariablePrefix);
        Assert.Equal(Parent.Schemas, followUp.Server.Schemas);
        Assert.Equal(Parent.ObjectTypes, followUp.Server.ObjectTypes);
        Assert.Equal(Parent.Encrypt, followUp.Server.Encrypt);
        Assert.Equal("SQLPROD01", followUp.DiscoveredFrom);
    }

    [Fact]
    public void Plan_LinkPinningACatalog_ExtractsOnlyThatDatabase()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SALES_LINK", catalog: "Sales.Db")], Enabled, [Parent]);

        LinkedServerFollowUp followUp = Assert.Single(plan.FollowUps);
        Assert.Equal("Sales.Db", followUp.Catalog);
        Assert.NotNull(followUp.Server.Databases);
        Assert.True(followUp.Server.Databases!.IsAllowed("Sales.Db"));
        Assert.False(followUp.Server.Databases!.IsAllowed("SalesXDb"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_ExcludesLinkedServersFromExplicitAndInheritedTypes(bool inheritDefaults)
    {
        string[] types = ["Tables", "linkedservers", "Views"];
        ObjectFilterSet defaults = new() { ObjectTypes = types };
        ServerConfig parent = Parent with { ObjectTypes = inheritDefaults ? null : types };

        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            parent, "svc_syncsql", [Link("SQLPROD02")], Enabled, [parent], defaults);

        ServerConfig remote = Assert.Single(plan.FollowUps).Server;
        Assert.Equal(["Tables", "Views"], EffectiveFilters.Resolve(defaults, remote).ObjectTypes);
        Assert.Equal(types, EffectiveFilters.Resolve(defaults, parent).ObjectTypes);
    }

    [Fact]
    public void Plan_OnlyLinkedServers_DoesNotFallBackToDefaults()
    {
        ServerConfig parent = Parent with { ObjectTypes = ["LinkedServers"] };
        ObjectFilterSet defaults = new() { ObjectTypes = ["Tables", "LinkedServers"] };

        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            parent, "svc_syncsql", [Link("SQLPROD02")], Enabled, [parent], defaults);

        Assert.Empty(EffectiveFilters.Resolve(defaults, Assert.Single(plan.FollowUps).Server).ObjectTypes);
        Assert.Equal(["LinkedServers"], parent.ObjectTypes);
    }

    [Fact]
    public void Plan_RestrictToLinkedCatalogOff_KeepsTheParentsDatabaseFilter()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("SALES_LINK", catalog: "SalesDb")],
            new LinkedServerDiscoveryConfig { Enabled = true, RestrictToLinkedCatalog = false },
            [Parent]);

        LinkedServerFollowUp followUp = Assert.Single(plan.FollowUps);
        Assert.Null(followUp.Catalog);
        Assert.Equal(Parent.Databases, followUp.Server.Databases);
    }

    [Fact]
    public void Plan_LinkMappedToADifferentRemoteLogin_IsSkipped()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("SQLPROD02", remoteLogins: ["reporting_reader"], usesLocalLogin: false)],
            Enabled,
            [Parent]);

        Assert.Empty(plan.FollowUps);
        SkippedLinkedServer skipped = Assert.Single(plan.Skipped);
        Assert.Contains("reporting_reader", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_LinkMappedToTheSameUsername_IsFollowed()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("SQLPROD02", remoteLogins: ["SVC_SyncSql"], usesLocalLogin: false)],
            Enabled,
            [Parent]);

        Assert.Single(plan.FollowUps);
    }

    [Fact]
    public void Plan_RequireMatchingLoginOff_FollowsADifferentLoginAnyway()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("SQLPROD02", remoteLogins: ["reporting_reader"], usesLocalLogin: false)],
            new LinkedServerDiscoveryConfig { Enabled = true, RequireMatchingLogin = false },
            [Parent]);

        Assert.Single(plan.FollowUps);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Plan_NonSqlServerLink_IsSkipped()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("ORACLE_LINK", product: "Oracle", provider: "OraOLEDB.Oracle")],
            Enabled,
            [Parent]);

        Assert.Empty(plan.FollowUps);
        Assert.Contains("not a SQL Server link", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_HostAlreadyConfigured_IsSkipped()
    {
        ServerConfig alreadyConfigured = Parent with
        {
            Name = "SQLPROD02_FINANCE",
            Host = "sqlprod02.example.com",
            CredentialsVariablePrefix = "SQLPROD02_FINANCE",
        };

        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SQLPROD02")], Enabled, [Parent, alreadyConfigured]);

        Assert.Empty(plan.FollowUps);
        Assert.Contains("already covered", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_NameCollidingWithAConfiguredServer_IsSuffixed()
    {
        ServerConfig sameName = Parent with { Name = "SQLPROD02", Host = "elsewhere.example.com", CredentialsVariablePrefix = "OTHER" };

        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SQLPROD02")], Enabled, [Parent, sameName]);

        Assert.Equal("SQLPROD02_2", Assert.Single(plan.FollowUps).Server.Name);
    }

    [Fact]
    public void Plan_DataSourceWithAPort_UsesIt()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent, "svc_syncsql", [Link("SQLPROD02", dataSource: "sqlprod02.example.com,14330")], Enabled, [Parent]);

        LinkedServerFollowUp followUp = Assert.Single(plan.FollowUps);
        Assert.Equal("sqlprod02.example.com", followUp.Server.Host);
        Assert.Equal(14330, followUp.Server.Port);
    }

    [Fact]
    public void Plan_NamedInstanceDataSource_KeepsTheInstanceAndDropsTheInheritedPort()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent with { Port = null }, "svc_syncsql", [Link("SQLPROD02", dataSource: @"SQLPROD02\FINANCE")], Enabled, [Parent]);

        LinkedServerFollowUp followUp = Assert.Single(plan.FollowUps);
        Assert.Equal(@"SQLPROD02\FINANCE", followUp.Server.Host);
        Assert.Null(followUp.Server.Port);
    }

    [Fact]
    public void Plan_LinkNameFilter_IsHonoured()
    {
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            Parent,
            "svc_syncsql",
            [Link("SQLPROD02"), Link("REPORTING", dataSource: "reporting.example.com")],
            new LinkedServerDiscoveryConfig { Enabled = true, LinkNames = new NameFilter { Exclude = ["^REPORTING$"] } },
            [Parent]);

        Assert.Equal("SQLPROD02", Assert.Single(plan.FollowUps).Server.Name);
        Assert.Contains("linkNames", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SQLPROD02", "example.com", "SQLPROD02.example.com", 1433)]
    [InlineData(" SQLPROD02,1444 ", " .example.com ", "SQLPROD02.example.com", 1444)]
    [InlineData(@"SQLPROD02\FINANCE", ".example.com", @"SQLPROD02.example.com\FINANCE", 1433)]
    [InlineData(@"tcp:SQLPROD02\FINANCE,1444", "example.com", @"tcp:SQLPROD02.example.com\FINANCE", 1444)]
    [InlineData("TCP:SQLPROD02,1444", "example.com", "TCP:SQLPROD02.example.com", 1444)]
    [InlineData(@"np:SQLPROD02\FINANCE", "example.com", @"np:SQLPROD02.example.com\FINANCE", 1433)]
    [InlineData("sqlprod02.other.com", "example.com", "sqlprod02.other.com", 1433)]
    [InlineData(@"sqlprod02.example.com\FINANCE", "example.com", @"sqlprod02.example.com\FINANCE", 1433)]
    [InlineData("SQLPROD02.", "example.com", "SQLPROD02.", 1433)]
    [InlineData("10.0.0.2", "example.com", "10.0.0.2", 1433)]
    [InlineData("tcp:[2001:db8::2],1444", "example.com", "tcp:[2001:db8::2]", 1444)]
    [InlineData("::1", "example.com", "::1", 1433)]
    [InlineData("127001", "example.com", "127001", 1433)]
    [InlineData("localhost", "example.com", "localhost", 1433)]
    [InlineData(@".\FINANCE", "example.com", @".\FINANCE", 1433)]
    [InlineData("(local)", "example.com", "(local)", 1433)]
    [InlineData(@"np:\\SQLPROD02\pipe\sql\query", "example.com", @"np:\\SQLPROD02\pipe\sql\query", 1433)]
    [InlineData("SQLPROD02", null, "SQLPROD02", 1433)]
    [InlineData("SQLPROD02", "", "SQLPROD02", 1433)]
    [InlineData("SQLPROD02", "  ", "SQLPROD02", 1433)]
    public void Plan_HostNameSuffix_QualifiesOnlyShortHosts(string dataSource, string? suffix, string expectedHost, int expectedPort)
    {
        ServerConfig parent = Parent with { HostNameSuffix = suffix };
        ServerConfig remote = Assert.Single(LinkedServerFollowUpPlanner.Plan(
            parent, "svc_syncsql", [Link("REMOTE", dataSource)], Enabled, [parent]).FollowUps).Server;

        Assert.Equal(expectedHost, remote.Host);
        Assert.Equal(expectedPort, remote.Port);
        Assert.Equal(suffix, remote.HostNameSuffix);
        Assert.Equal(Parent.Host, parent.Host);
    }

    [Fact]
    public void Plan_HostNameSuffix_IsInheritedByNestedDiscovery()
    {
        ServerConfig parent = Parent with { HostNameSuffix = "example.com", Port = null };
        ServerConfig remote = Assert.Single(LinkedServerFollowUpPlanner.Plan(
            parent, "svc_syncsql", [Link("REMOTE", @"SQLPROD02\FINANCE")], Enabled, [parent]).FollowUps).Server;
        ServerConfig nested = Assert.Single(LinkedServerFollowUpPlanner.Plan(
            remote, "svc_syncsql", [Link("NEXT", "SQLPROD03")], Enabled, [parent, remote]).FollowUps).Server;

        Assert.Equal(@"SQLPROD02.example.com\FINANCE", remote.Host);
        Assert.Null(remote.Port);
        Assert.Equal("SQLPROD03.example.com", nested.Host);
    }

    [Fact]
    public void Plan_HostNameSuffix_DeduplicatesQualifiedTargetsAndCycles()
    {
        ServerConfig parent = Parent with { HostNameSuffix = "example.com" };
        LinkedServerFollowUpPlan plan = LinkedServerFollowUpPlanner.Plan(
            parent, "svc_syncsql",
            [Link("REMOTE", "SQLPROD02"), Link("DUPLICATE", "sqlprod02.example.com"), Link("SELF", "SQLPROD01")],
            Enabled, [parent]);

        Assert.Equal("SQLPROD02.example.com", Assert.Single(plan.FollowUps).Server.Host);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.All(plan.Skipped, skipped => Assert.Contains("already covered", skipped.Reason, StringComparison.Ordinal));
    }
}
