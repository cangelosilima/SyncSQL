using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;

namespace SyncSql.Core.Tests.Credentials;

public class LayeredCredentialProviderTests
{
    private static EnvironmentCredentialProvider Environment(params (string Name, string Value)[] variables)
    {
        Dictionary<string, string> values = variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);
        return new EnvironmentCredentialProvider(name => values.GetValueOrDefault(name));
    }

    [Fact]
    public void Parameters_TakePrecedenceOverTheEnvironment()
    {
        LayeredCredentialProvider provider = new(
            ExplicitCredentialProvider.FromArguments(["SQLPROD01=from-parameter"], ["SQLPROD01=parameter-pw"]),
            Environment(("SQLPROD01_DB_USER", "from-environment"), ("SQLPROD01_DB_PASSWORD", "environment-pw")));

        Assert.Equal(new DatabaseCredentials("from-parameter", "parameter-pw"), provider.Resolve("SQLPROD01"));
    }

    [Fact]
    public void EachHalfResolvesIndependently()
    {
        LayeredCredentialProvider provider = new(
            ExplicitCredentialProvider.FromArguments(["SQLPROD01=from-parameter"], []),
            Environment(("SQLPROD01_DB_PASSWORD", "environment-pw")));

        Assert.Equal(new DatabaseCredentials("from-parameter", "environment-pw"), provider.Resolve("SQLPROD01"));
    }

    [Fact]
    public void FallsThroughToALowerLayerForAnUnknownPrefix()
    {
        LayeredCredentialProvider provider = new(
            ExplicitCredentialProvider.FromArguments(["SQLPROD01=svc"], ["SQLPROD01=pw"]),
            Environment(("ORAPROD01_DB_USER", "oracle_user"), ("ORAPROD01_DB_PASSWORD", "oracle_pw")));

        Assert.Equal(new DatabaseCredentials("oracle_user", "oracle_pw"), provider.Resolve("ORAPROD01"));
    }

    [Fact]
    public void Resolve_WithNothingAnywhere_NamesEverySourceTried()
    {
        LayeredCredentialProvider provider = new(
            ExplicitCredentialProvider.FromArguments([], []),
            Environment());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Resolve("SQLPROD01"));

        Assert.Contains("--db-user/--db-password", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SQLPROD01_DB_USER", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresAtLeastOneLayer()
    {
        Assert.Throws<ArgumentException>(() => new LayeredCredentialProvider());
    }
}
