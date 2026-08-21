using SyncSql.Core.Configuration;

namespace SyncSql.Core.Abstractions;

public sealed record GitPublishRequest
{
    public required ResolvedGitConfig GitConfig { get; init; }
    public required string StagingRoot { get; init; }
    public required string Token { get; init; }
    public required string WorkDir { get; init; }
    public string Summary { get; init; } = string.Empty;

    /// <summary>How many commits to clone. Keep at 1 unless the caller needs real history afterward (e.g. to mine it for a catalog before committing).</summary>
    public int CloneDepth { get; init; } = 1;

    /// <summary>
    /// Invoked with (workDir, pathPrefix) after the staged tree has been copied into workDir/pathPrefix
    /// but before `git add`/commit, so a caller can add more files (e.g. a generated catalog.json) into
    /// the same commit.
    /// </summary>
    public Func<string, string, CancellationToken, Task>? PostSyncHookAsync { get; init; }
}

public sealed record GitPublishResult
{
    /// <summary>False when extraction produced no diff since the last run - nothing was pushed.</summary>
    public required bool Published { get; init; }
}

/// <summary>
/// Full clone -> sync -> commit -> push flow: replaces config.git.pathPrefix in the target repo with the
/// freshly staged extraction tree (so dropped objects show up as deletions) and pushes the result. A
/// direct port of SyncSql.Git.psm1's Publish-SyncSqlToGit.
/// </summary>
public interface IGitRepository
{
    public Task<GitPublishResult> PublishAsync(GitPublishRequest request, CancellationToken cancellationToken);
}
