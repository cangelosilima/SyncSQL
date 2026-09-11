using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public sealed class GitHistoryMinerTests : IDisposable
{
    [Fact]
    public async Task MineAsync_NestedRemotePath_KeepsEarlierSchemaFirstHistory()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        const string id = "REMOTE/AppDb/Tables/dbo/Orders";
        const string priorPath = "REMOTE/AppDb/dbo/Tables/Orders.sql";
        const string currentPath = "ROOT/LinkedServers/REMOTE/AppDb/dbo/Tables/Orders.sql";
        string log = $"@@COMMIT@@old@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Original export\nobjects/{priorPath}\n";
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(args => args.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(args => args.Contains($"old:objects/{priorPath}")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "CREATE TABLE dbo.Orders (Id int);", string.Empty));
        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string> { id },
            ObjectPaths = new Dictionary<string, string> { [currentPath] = id },
        }, CancellationToken.None);
        Assert.Equal("CREATE TABLE dbo.Orders (Id int);", Assert.Single(result.ObjectHistory[id].Versions).Ddl);
    }
    private readonly string _repoRoot = Directory.CreateTempSubdirectory("syncsql-git-").FullName;
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly GitHistoryMiner _miner;

    [Fact]
    public async Task MineAsync_CommitWithoutSubjectKeepsEmptyMessage()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        const string id = "SQL/db/Tables/dbo/t";
        _processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, $"@@COMMIT@@abc@@COMMIT@@2026-01-01T00:00:00Z\nobjects/{id}.sql\n", ""));
        var result = await _miner.MineAsync(new GitHistoryMiningRequest { RepoRoot = _repoRoot, PathPrefix = "objects", KnownObjectIds = new HashSet<string> { id }, MaxHistoryContentCalls = 0 }, CancellationToken.None);
        Assert.Equal("", Assert.Single(result.RecentChanges).Message);
    }

    public GitHistoryMinerTests()
    {
        _miner = new GitHistoryMiner(_processRunner, NullLogger<GitHistoryMiner>.Instance);
    }

    [Fact]
    public async Task MineAsync_LayoutMove_PreservesIdentityAndReadsEachHistoricalPath()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        const string id = "SQLPROD01/AppDb/Tables/dbo/Orders";
        const string newPath = "SQLPROD01/AppDb/dbo/Tables/Orders.sql";
        string log = $"@@COMMIT@@new@@COMMIT@@2026-02-01T00:00:00+00:00@@COMMIT@@Move layout\nobjects/{newPath}\nobjects/{id}.sql\n" +
            $"@@COMMIT@@old@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Create table\nobjects/{id}.sql\n";
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains($"new:objects/{newPath}")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "CREATE TABLE dbo.Orders (Id INT, Total INT);", string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains($"old:objects/{id}.sql")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "CREATE TABLE dbo.Orders (Id INT);", string.Empty));
        var result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string> { id },
            ObjectPaths = new Dictionary<string, string> { [newPath] = id },
        }, CancellationToken.None);
        Assert.Equal(2, result.RecentChanges.Count);
        Assert.All(result.RecentChanges, commit => Assert.Equal(id, Assert.Single(commit.ObjectIds)));
        var history = result.ObjectHistory[id];
        Assert.Equal(2, history.ChangeCount);
        Assert.Contains("Total", history.Versions[0].Ddl);
        Assert.Equal("CREATE TABLE dbo.Orders (Id INT);", history.Versions[1].Ddl);
        Assert.Empty(result.CoChangePairs);
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

    [Fact]
    public async Task MineAsync_EmptyPathPrefix_TreatsRepositoryRootAsTheExtractedTree()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        // The default layout: no wrapping folder, so the first path segment is the server name.
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Add Orders table\n" +
            "SQLPROD01/AppDb/Tables/dbo/Orders.sql\n" +
            "catalog.json\n";

        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("show")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "-- Engine:   mssql\n\nCREATE TABLE dbo.Orders (Id INT);", string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = string.Empty,
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        // The object id is the whole path with nothing stripped off the front, and catalog.json - a
        // sibling of the server directories now, not a file in another folder - is still ignored.
        Assert.Single(result.RecentChanges);
        Assert.Equal("SQLPROD01/AppDb/Tables/dbo/Orders", Assert.Single(result.RecentChanges[0].ObjectIds));

        // git rejects an empty pathspec, so a root-level tree has to be asked for as "." instead.
        await _processRunner.Received().RunAsync(
            "git",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains("log") && a[a.Count - 1] == "."),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());

        // ...and the content fetch must not end up asking for "abc123:/SQLPROD01/...".
        await _processRunner.Received().RunAsync(
            "git",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains("abc123:SQLPROD01/AppDb/Tables/dbo/Orders.sql")),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MineAsync_GitOutputSeparatedByCrlf_ParsesDdlAndSubjectWithoutStrayCarriageReturns()
    {
        // What a Windows capture used to look like: the log's subject is the last field on its
        // line, and every marker line in the shown file ends in CR - which no '$'-anchored pattern
        // in the object-file format matches, so the whole file collapsed into one DDL blob.
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Add Orders table\r\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\r\n";
        string show =
            "-- Engine:   mssql\r\n" +
            "\r\n" +
            "CREATE TABLE dbo.Orders (Id INT);\r\n" +
            "\r\n" +
            "-- === Columns ===\r\n" +
            "-- [col] Id|int\r\n";

        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("show")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, show, string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        Assert.Equal("Add Orders table", result.RecentChanges[0].Message);
        ObjectHistoryInfo info = Assert.Single(result.ObjectHistory).Value;
        Assert.Equal("CREATE TABLE dbo.Orders (Id INT);", info.Versions[0].Ddl);
    }

    [Fact]
    public async Task MineAsync_GitShowOutputStartingWithAUtf8Bom_StillParsesDdl()
    {
        // `git show` hands back the blob's bytes, byte-order mark included - files written by the
        // PowerShell extractor have one, and it lands on the first header line.
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        string log =
            "@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@Add Orders table\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n";

        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("log")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));
        _processRunner.RunAsync("git", Arg.Is<IReadOnlyList<string>>(a => a.Contains("show")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "\uFEFF-- Engine:   mssql\n\nCREATE TABLE dbo.Orders (Id INT);", string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        ObjectHistoryInfo info = Assert.Single(result.ObjectHistory).Value;
        Assert.Equal("CREATE TABLE dbo.Orders (Id INT);", info.Versions[0].Ddl);
    }

    [Fact]
    public async Task MineAsync_NonAsciiCommitSubject_IsPreservedVerbatim()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        const string subject = "Adiciona descrição da coleção de pedidos";
        string log =
            $"@@COMMIT@@abc123@@COMMIT@@2026-01-01T00:00:00+00:00@@COMMIT@@{subject}\n" +
            "objects/SQLPROD01/AppDb/Tables/dbo/Orders.sql\n";

        _processRunner.RunAsync("git", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, log, string.Empty));

        GitHistoryMiningResult result = await _miner.MineAsync(new GitHistoryMiningRequest
        {
            RepoRoot = _repoRoot,
            PathPrefix = "objects",
            KnownObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQLPROD01/AppDb/Tables/dbo/Orders" },
        }, CancellationToken.None);

        Assert.Equal(subject, result.RecentChanges[0].Message);
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
