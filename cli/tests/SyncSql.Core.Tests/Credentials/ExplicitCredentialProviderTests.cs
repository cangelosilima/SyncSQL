using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;

namespace SyncSql.Core.Tests.Credentials;

public class ExplicitCredentialProviderTests
{
    [Fact]
    public void FromArguments_ParsesPrefixValuePairs()
    {
        ExplicitCredentialProvider provider = ExplicitCredentialProvider.FromArguments(
            ["SQLPROD01=svc_sync", "ORAPROD01=oracle_user"],
            ["SQLPROD01=pw1", "ORAPROD01=pw2"]);

        Assert.Equal(new DatabaseCredentials("svc_sync", "pw1"), provider.Resolve("SQLPROD01"));
        Assert.Equal(new DatabaseCredentials("oracle_user", "pw2"), provider.Resolve("ORAPROD01"));
    }

    [Fact]
    public void FromArguments_SplitsOnFirstSeparatorOnly_SoPasswordsMayContainEquals()
    {
        ExplicitCredentialProvider provider = ExplicitCredentialProvider.FromArguments(
            ["SQLPROD01=svc_sync"],
            ["SQLPROD01=a=b==c"]);

        Assert.Equal("a=b==c", provider.Read("SQLPROD01").Password);
    }

    [Fact]
    public void FromArguments_MatchesPrefixCaseInsensitively()
    {
        ExplicitCredentialProvider provider = ExplicitCredentialProvider.FromArguments(["sqlprod01=svc"], ["SQLPROD01=pw"]);

        Assert.Equal(new DatabaseCredentials("svc", "pw"), provider.Resolve("SqlProd01"));
    }

    [Fact]
    public void FromArguments_ReadsUnknownPrefixAsNothing()
    {
        ExplicitCredentialProvider provider = ExplicitCredentialProvider.FromArguments(["SQLPROD01=svc"], ["SQLPROD01=pw"]);

        Assert.Equal(PartialCredentials.None, provider.Read("ORAPROD01"));
    }

    [Fact]
    public void FromArguments_WithoutSeparator_Throws()
    {
        CredentialParseException exception = Assert.Throws<CredentialParseException>(
            () => ExplicitCredentialProvider.FromArguments(["svc_sync"], []));

        Assert.Contains("--db-user", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PREFIX=value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromArguments_WithMalformedPassword_DoesNotEchoTheSecret()
    {
        CredentialParseException exception = Assert.Throws<CredentialParseException>(
            () => ExplicitCredentialProvider.FromArguments([], ["hunter2-super-secret"]));

        Assert.DoesNotContain("hunter2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=svc_sync")]
    [InlineData("SQLPROD01=")]
    public void FromArguments_WithEmptySide_Throws(string argument)
    {
        Assert.Throws<CredentialParseException>(() => ExplicitCredentialProvider.FromArguments([argument], []));
    }

    [Fact]
    public void FromArguments_WithDuplicatePrefix_Throws()
    {
        CredentialParseException exception = Assert.Throws<CredentialParseException>(
            () => ExplicitCredentialProvider.FromArguments(["SQLPROD01=a", "sqlprod01=b"], []));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromArguments_WithNothingPassed_IsEmpty()
    {
        Assert.True(ExplicitCredentialProvider.FromArguments([], []).IsEmpty);
    }

    [Fact]
    public void Resolve_WithOnlyOneHalf_ThrowsNamingTheSource()
    {
        ExplicitCredentialProvider provider = ExplicitCredentialProvider.FromArguments(["SQLPROD01=svc"], []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Resolve("SQLPROD01"));

        Assert.Contains("SQLPROD01", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--db-user/--db-password", exception.Message, StringComparison.Ordinal);
    }
}
