using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;

namespace SyncSql.Core.Tests.Credentials;

public class CredentialsFileProviderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("syncsql-credentials-tests").FullName;

    private async Task<string> WriteFileAsync(string json)
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ReadsCredentialsKeyedByPrefix()
    {
        string path = await WriteFileAsync("""
            {
              "SQLPROD01": { "user": "svc_sync", "password": "pw1" },
              "ORAPROD01": { "user": "oracle_user", "password": "pw2" }
            }
            """);

        CredentialsFileProvider provider = await CredentialsFileProvider.LoadAsync(path);

        Assert.Equal(new DatabaseCredentials("svc_sync", "pw1"), provider.Resolve("sqlprod01"));
        Assert.Equal(new DatabaseCredentials("oracle_user", "pw2"), provider.Resolve("ORAPROD01"));
    }

    [Fact]
    public async Task LoadAsync_KeepsAPartialEntryPartial_SoALowerLayerCanCompleteIt()
    {
        string path = await WriteFileAsync("""{ "SQLPROD01": { "user": "svc_sync" } }""");

        CredentialsFileProvider provider = await CredentialsFileProvider.LoadAsync(path);

        Assert.Equal(new PartialCredentials("svc_sync", null), provider.Read("SQLPROD01"));
    }

    [Fact]
    public async Task LoadAsync_ReadsAnUnknownPrefixAsNothing()
    {
        string path = await WriteFileAsync("""{ "SQLPROD01": { "user": "svc_sync", "password": "pw" } }""");

        CredentialsFileProvider provider = await CredentialsFileProvider.LoadAsync(path);

        Assert.Equal(PartialCredentials.None, provider.Read("ORAPROD01"));
    }

    [Fact]
    public async Task LoadAsync_WithMissingFile_Throws()
    {
        string path = Path.Combine(_directory, "does-not-exist.json");

        CredentialParseException exception = await Assert.ThrowsAsync<CredentialParseException>(() => CredentialsFileProvider.LoadAsync(path));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithMalformedJson_Throws()
    {
        string path = await WriteFileAsync("{ not json");

        await Assert.ThrowsAsync<CredentialParseException>(() => CredentialsFileProvider.LoadAsync(path));
    }

    [Fact]
    public async Task Resolve_WithAMissingHalf_NamesTheFile()
    {
        string path = await WriteFileAsync("""{ "SQLPROD01": { "user": "svc_sync" } }""");

        CredentialsFileProvider provider = await CredentialsFileProvider.LoadAsync(path);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Resolve("SQLPROD01"));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
