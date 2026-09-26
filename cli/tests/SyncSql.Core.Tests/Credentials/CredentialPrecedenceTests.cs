using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;

namespace SyncSql.Core.Tests.Credentials;

public sealed class CredentialPrecedenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LowerPriorityValuesOnlyFillTheMissingHalf(bool usernameFirst)
    {
        ICredentialProvider first = Substitute.For<ICredentialProvider>();
        ICredentialProvider second = Substitute.For<ICredentialProvider>();
        ICredentialProvider unused = Substitute.For<ICredentialProvider>();
        first.Read("SQL").Returns(usernameFirst ? new PartialCredentials("preferred-user", null) : new PartialCredentials(null, "preferred-password"));
        second.Read("SQL").Returns(new PartialCredentials("fallback-user", "fallback-password"));

        LayeredCredentialProvider provider = new(first, second, unused);
        Assert.Equal(usernameFirst
            ? new PartialCredentials("preferred-user", "fallback-password")
            : new PartialCredentials("fallback-user", "preferred-password"), provider.Read("SQL"));
        unused.DidNotReceive().Read(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyValuesDoNotPreventFallback(string? empty)
    {
        ICredentialProvider first = Substitute.For<ICredentialProvider>();
        ICredentialProvider second = Substitute.For<ICredentialProvider>();
        first.Read("SQL").Returns(new PartialCredentials(empty, empty));
        second.Read("SQL").Returns(new PartialCredentials("user", "password"));
        Assert.Equal(new PartialCredentials("user", "password"), new LayeredCredentialProvider(first, second).Read("SQL"));
    }

    [Fact]
    public async Task FilePropertiesAreCaseInsensitiveAndAllowTrailingCommas()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"SQL":{"USER":"user","PASSWORD":"password",},}""");
            CredentialsFileProvider provider = await CredentialsFileProvider.LoadAsync(path);
            Assert.Equal(new PartialCredentials("user", "password"), provider.Read("sql"));
        }
        finally { File.Delete(path); }
    }
}
