using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public sealed class ServerConfigurationContractTests
{
    private static readonly ServerConfig Server = new()
    {
        Name = "Reporting",
        Host = "db",
        Type = DatabaseEngine.MsSql,
        CredentialsVariablePrefix = "DB",
    };

    [Fact]
    public void OracleIdentityPreservesServicePortAndNormalizesAliases()
    {
        ServerIdentity identity = ServerIdentity.FromConfig(Server with
        {
            Type = DatabaseEngine.Oracle,
            Host = " oracle.example ",
            Port = 1522,
            ServiceName = "Sales",
            Aliases = [" alias ", "ALIAS"],
            Tags = [" prod ", "PROD", "", " ", null!],
        });
        Assert.Equal("ORACLE.EXAMPLE:1522/Sales", identity.Endpoint);
        Assert.Equal(["ALIAS", "ORACLE.EXAMPLE:1522/Sales"], identity.Addresses);
        Assert.Equal(["prod"], identity.Tags);
        Assert.True(identity.Matches(" alias "));
        Assert.False(identity.Matches("alias,1433"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void ValidBoundaryPortsStillApplyTheDnsSuffix(int port) =>
        Assert.Equal($"SQL.EXAMPLE.COM,{port}", ServerIdentity.NormalizeSqlAddress($"sql,{port}", suffix: "example.com"));

    [Fact]
    public void DefaultObjectNameFilterIsInheritedWhenServerHasNoOverride()
    {
        EffectiveFilters filters = EffectiveFilters.Resolve(new() { ObjectNames = new() { Exclude = ["^private_"] } }, Server);
        Assert.False(filters.ObjectNames.IsAllowed("private_orders"));
        Assert.True(filters.ObjectNames.IsAllowed("orders"));
    }

    [Fact]
    public void OwnerlessLinkOverrideAppliesToAnOwnedLinkAndRecordsExactEvidence()
    {
        ServerConfig config = Server with
        {
            LinkTargets = [
                new() { Name = "Other", DataSource = "wrong" },
                new() { Name = "DL", Owner = "OTHER", DataSource = "wrong-owner" },
                new() { Name = "DL", DataSource = "sql,1444", Database = "Orders", DefaultSchema = "sales", TargetEngine = DatabaseEngine.MsSql },
            ],
        };
        LinkMetadata result = LinkTargetEnrichment.Apply(config, "dl", "APP", new());
        Assert.Equal("sql,1444", result.DataSource);
        Assert.Equal("Orders", result.Database);
        Assert.Equal("sales", result.DefaultSchema);
        Assert.Equal(DatabaseEngine.MsSql, result.TargetEngine);
        Assert.Equal([
            new LinkEvidence("dataSource", "sql,1444", "config:linkTargets/APP/dl"),
            new LinkEvidence("database", "Orders", "config:linkTargets/APP/dl"),
            new LinkEvidence("defaultSchema", "sales", "config:linkTargets/APP/dl"),
            new LinkEvidence("targetEngine", "mssql", "config:linkTargets/APP/dl"),
        ], result.Evidence);
    }
}
