using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Git.Tests;

public sealed class GitRepositoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-gitrepo-").FullName;
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly GitRepository _repository;

    public GitRepositoryTests()
    {
        _repository = new GitRepository(_processRunner, NullLogger<GitRepository>.Instance);
        _processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));
    }

    private string StagingRoot => Path.Combine(_root, "staging");
    private string WorkDir => Path.Combine(_root, "work");

    private static ResolvedGitConfig Config(string? remoteUrl = "https://example.invalid/repo.git") => new()
    {
        RemoteUrl = remoteUrl,
        Branch = "main",
        PathPrefix = "objects",
        CommitUserName = "SyncSQL Bot",
        CommitUserEmail = "syncsql-bot@example.com",
        CommitMessage = "chore(sync): update database objects",
    };

    private void SetGitResult(Func<IReadOnlyList<string>, bool> matches, ProcessResult result) =>
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => matches(a)), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private void SetStatusPorcelain(string output) => SetGitResult(a => a[0] == "status", new ProcessResult(0, output, string.Empty));

    [Fact]
    public async Task PublishAsync_ChangesDetected_CommitsAndPushesAndReturnsPublishedTrue()
    {
        SetStatusPorcelain(" M objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n");

        GitPublishResult result = await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
        }, CancellationToken.None);

        Assert.True(result.Published);
        await _processRunner.Received(1).RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a[0] == "commit"), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        await _processRunner.Received(1).RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a[0] == "push"), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_NoChangesDetected_ReturnsPublishedFalseWithoutCommittingOrPushing()
    {
        SetStatusPorcelain(string.Empty);

        GitPublishResult result = await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
        }, CancellationToken.None);

        Assert.False(result.Published);
        await _processRunner.DidNotReceive().RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a[0] == "commit"), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        await _processRunner.DidNotReceive().RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a[0] == "push"), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_BranchNotFoundOnRemote_FallsBackToDefaultCloneAndChecksOutBranch()
    {
        SetGitResult(a => a[0] == "clone" && a.Contains("--branch"), new ProcessResult(128, string.Empty, "fatal: Remote branch main not found"));
        SetGitResult(a => a[0] == "clone" && !a.Contains("--branch"), new ProcessResult(0, string.Empty, string.Empty));
        SetStatusPorcelain(string.Empty);

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
        }, CancellationToken.None);

        await _processRunner.Received(1).RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a[0] == "checkout" && a.Contains("-B") && a.Contains("main")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_NoRemoteUrlAndNoCiEnvironment_ThrowsInvalidOperationException()
    {
        string? previousProtocol = Environment.GetEnvironmentVariable("CI_SERVER_PROTOCOL");
        string? previousHost = Environment.GetEnvironmentVariable("CI_SERVER_HOST");
        string? previousPath = Environment.GetEnvironmentVariable("CI_PROJECT_PATH");
        Environment.SetEnvironmentVariable("CI_SERVER_PROTOCOL", null);
        Environment.SetEnvironmentVariable("CI_SERVER_HOST", null);
        Environment.SetEnvironmentVariable("CI_PROJECT_PATH", null);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.PublishAsync(new GitPublishRequest
            {
                GitConfig = Config(remoteUrl: null),
                StagingRoot = StagingRoot,
                Token = "s3cr3t-token",
                WorkDir = WorkDir,
            }, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI_SERVER_PROTOCOL", previousProtocol);
            Environment.SetEnvironmentVariable("CI_SERVER_HOST", previousHost);
            Environment.SetEnvironmentVariable("CI_PROJECT_PATH", previousPath);
        }
    }

    [Fact]
    public async Task PublishAsync_NoRemoteUrlButCiEnvironmentPresent_DerivesRemoteUrl()
    {
        string? previousProtocol = Environment.GetEnvironmentVariable("CI_SERVER_PROTOCOL");
        string? previousHost = Environment.GetEnvironmentVariable("CI_SERVER_HOST");
        string? previousPath = Environment.GetEnvironmentVariable("CI_PROJECT_PATH");
        Environment.SetEnvironmentVariable("CI_SERVER_PROTOCOL", "https");
        Environment.SetEnvironmentVariable("CI_SERVER_HOST", "gitlab.example.invalid");
        Environment.SetEnvironmentVariable("CI_PROJECT_PATH", "group/project");
        SetStatusPorcelain(string.Empty);

        try
        {
            await _repository.PublishAsync(new GitPublishRequest
            {
                GitConfig = Config(remoteUrl: null),
                StagingRoot = StagingRoot,
                Token = "s3cr3t-token",
                WorkDir = WorkDir,
            }, CancellationToken.None);

            await _processRunner.Received(1).RunAsync(
                "git",
                Arg.Is<IReadOnlyList<string>>(a => a.Contains("https://gitlab.example.invalid/group/project.git")),
                Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI_SERVER_PROTOCOL", previousProtocol);
            Environment.SetEnvironmentVariable("CI_SERVER_HOST", previousHost);
            Environment.SetEnvironmentVariable("CI_PROJECT_PATH", previousPath);
        }
    }

    [Fact]
    public async Task PublishAsync_GitCommandFails_ThrowsInvalidOperationExceptionWithGitStderr()
    {
        SetGitResult(a => a[0] == "config", new ProcessResult(1, string.Empty, "fatal: could not set config"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
        }, CancellationToken.None));

        Assert.Contains("could not set config", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_TokenNeverPassedAsCommandLineArgument()
    {
        SetStatusPorcelain(" M objects/Orders.sql\n");
        const string Token = "super-secret-token-value";

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = Token,
            WorkDir = WorkDir,
        }, CancellationToken.None);

        foreach (ICall call in _processRunner.ReceivedCalls())
        {
            object?[] callArguments = call.GetArguments();
            IReadOnlyList<string>? gitArgs = callArguments.OfType<IReadOnlyList<string>>().FirstOrDefault();
            Assert.DoesNotContain(Token, gitArgs ?? []);
        }
    }

    [Fact]
    public async Task PublishAsync_TokenPassedToGitOnlyThroughPerCallEnvironmentVariables()
    {
        SetStatusPorcelain(string.Empty);
        const string Token = "super-secret-token-value";

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = Token,
            WorkDir = WorkDir,
        }, CancellationToken.None);

        await _processRunner.Received().RunAsync(
            "git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(),
            Arg.Is<IReadOnlyDictionary<string, string>?>(env => env != null && env["SYNCSQL_GIT_PASSWORD"] == Token),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_CopiesStagingRootContentsIntoWorkDirPathPrefix()
    {
        Directory.CreateDirectory(Path.Combine(StagingRoot, "SQLPROD01", "AppDb", "Tables", "dbo"));
        string sourceFile = Path.Combine(StagingRoot, "SQLPROD01", "AppDb", "Tables", "dbo", "Orders.sql");
        await File.WriteAllTextAsync(sourceFile, "CREATE TABLE dbo.Orders (Id INT);");
        SetStatusPorcelain(" A objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n");

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
        }, CancellationToken.None);

        string copiedFile = Path.Combine(WorkDir, "objects", "SQLPROD01", "AppDb", "Tables", "dbo", "Orders.sql");
        Assert.True(File.Exists(copiedFile));
        Assert.Equal("CREATE TABLE dbo.Orders (Id INT);", await File.ReadAllTextAsync(copiedFile));
    }

    [Fact]
    public async Task PublishAsync_PostSyncHook_InvokedBeforeCommitWithWorkDirAndPathPrefix()
    {
        SetStatusPorcelain(" A objects/extra.json\n");
        List<(string WorkDir, string PathPrefix)> hookCalls = [];

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
            PostSyncHookAsync = (workDir, pathPrefix, ct) =>
            {
                hookCalls.Add((workDir, pathPrefix));
                return Task.CompletedTask;
            },
        }, CancellationToken.None);

        (string HookWorkDir, string PathPrefix) call = Assert.Single(hookCalls);
        Assert.Equal(WorkDir, call.HookWorkDir);
        Assert.Equal("objects", call.PathPrefix);
    }

    [Fact]
    public async Task PublishAsync_SummaryProvided_AppendsToCommitMessage()
    {
        SetStatusPorcelain(" A objects/Orders.sql\n");

        await _repository.PublishAsync(new GitPublishRequest
        {
            GitConfig = Config(),
            StagingRoot = StagingRoot,
            Token = "s3cr3t-token",
            WorkDir = WorkDir,
            Summary = "3 objects changed",
        }, CancellationToken.None);

        await _processRunner.Received(1).RunAsync(
            "git",
            Arg.Is<IReadOnlyList<string>>(a => a[0] == "commit" && a.Any(s => s.Contains("3 objects changed", StringComparison.Ordinal))),
            Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
