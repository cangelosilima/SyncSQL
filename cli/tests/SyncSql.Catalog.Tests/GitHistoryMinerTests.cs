using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public sealed class GitHistoryMinerTests : IDisposable
{
    private readonly string _repoRoot = Directory.CreateTempSubdirectory("syncsql-git-").FullName;
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly GitHistoryMiner _miner;

    public GitHistoryMinerTests()
    {
        _miner = new GitHistoryMiner(_processRunner, NullLogger<GitHistoryMiner>.Instance);
    }

    [Fact]
    public async Task MineAsync_NoGitDirectory_ReturnsEmptyWithoutCallingProcessRunner()
    {
        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        }, CancellationToken.None);

        Assert.Empty(result.RecentChanges);
        Assert.Empty(result.CoChangePairs);
        await _processRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!);
    }

    [Fact]
    public async Task MineAsync_GitLogFails_ReturnsEmptyRatherThanThrowing()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        _processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(128, string.Empty, "fatal: not a git repository"));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        }, CancellationToken.None);

        Assert.Empty(result.RecentChanges);
    }

    [Fact]
    public async Task MineAsync_SingleCommitTouchingOneKnownObject_ProducesChangeCountAndRecentChange()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Add Orders table\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n";

        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("show")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "-- Engine:   mssql\n\nCREATE TABLE dbo.Orders (Id INT);", string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        Assert.Single(result.RecentChanges);
        Assert.Equal("abc123", result.RecentChanges[0].Sha);
        ObjectHistoryInfo info = Assert.Single(result.ObjectHistory).Value;
        Assert.Equal(1, info.ChangeCount);
        Assert.Single(info.Versions);
        Assert.Contains("CREATE TABLE dbo.Orders", info.Versions[0].Ddl);
    }

    [Fact]
    public async Task MineAsync_CommitTouchingUnknownFile_IsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Unrelated change\n" +
            "README.md\n";

        _processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        Assert.Empty(result.RecentChanges);
        Assert.Empty(result.ObjectHistory);
    }

    [Fact]
    public async Task MineAsync_CommitTouchingTwoKnownObjects_ProducesCoChangePair()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Add both\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/OrderLines.sql\n";

        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("show")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "irrelevant", string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SQLPROD01/AppDb/Tables/dbo/Orders",
                "SQLPROD01/AppDb/Tables/dbo/OrderLines",
            },
        }, CancellationToken.None);

        CoChangePair pair = Assert.Single(result.CoChangePairs);
        Assert.Equal(1, pair.Count);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repoRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
