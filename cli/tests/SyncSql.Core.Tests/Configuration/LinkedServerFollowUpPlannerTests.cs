using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public class LinkedServerFollowUpPlannerTests
{
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
}
