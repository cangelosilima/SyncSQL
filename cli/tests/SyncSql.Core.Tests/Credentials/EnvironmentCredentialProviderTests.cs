using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;

namespace SyncSql.Core.Tests.Credentials;

public class EnvironmentCredentialProviderTests
{
    [Fact]
    public void Read_UsesThePrefixedVariableNames()
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["SQLPROD01_DB_USER"] = "svc_sync",
            ["SQLPROD01_DB_PASSWORD"] = "pw",
        };
        EnvironmentCredentialProvider provider = new(name => environment.GetValueOrDefault(name));

        Assert.Equal(new DatabaseCredentials("svc_sync", "pw"), provider.Resolve("SQLPROD01"));
    }

    [Fact]
    public void Resolve_WithMissingVariables_NamesBothOfThem()
    {
        EnvironmentCredentialProvider provider = new(_ => null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Resolve("SQLPROD01"));

        Assert.Contains("SQLPROD01_DB_USER", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SQLPROD01_DB_PASSWORD", exception.Message, StringComparison.Ordinal);
    }
}
