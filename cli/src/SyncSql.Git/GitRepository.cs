using System.Globalization;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Git;

/// <summary>
/// Full clone -&gt; sync -&gt; commit -&gt; push flow, shelling out to the `git` CLI via <see cref="IProcessRunner"/>
/// (mockable for unit tests, and keeps this project free of a native libgit2 dependency). A direct port
/// of SyncSql.Git.psm1's Publish-SyncSqlToGit. The push token is only ever handed to git through
/// GIT_ASKPASS plus per-invocation environment variables - never a CLI argument, never mutated onto this
/// process's own environment, and never embedded in the remote URL - so it can't leak through a process
/// listing, `git remote -v`, or shell history.
/// </summary>
public sealed class GitRepository(IProcessRunner processRunner, ILogger<GitRepository> logger) : IGitRepository
{
    public async Task<GitPublishResult> PublishAsync(GitPublishRequest request, CancellationToken cancellationToken)
    {
        ResolvedGitConfig config = request.GitConfig;
        string remoteUrl = ResolveRemoteUrl(config.RemoteUrl);

        if (Directory.Exists(request.WorkDir))
        {
            Directory.Delete(request.WorkDir, recursive: true);
        }
        Directory.CreateDirectory(request.WorkDir);

        string askPassPath = WriteAskPassScript();
        Dictionary<string, string> gitEnv = new(StringComparer.Ordinal)
        {
            ["GIT_ASKPASS"] = askPassPath,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["SYNCSQL_GIT_USERNAME"] = "oauth2",
            ["SYNCSQL_GIT_PASSWORD"] = request.Token,
        };

        try
        {
            string cloneDepth = request.CloneDepth.ToString(CultureInfo.InvariantCulture);

            logger.LogInformation("Cloning target repository (branch '{Branch}', depth {Depth})", config.Branch, request.CloneDepth);
            ProcessResult cloneResult = await RunGitAsync(
                null,
                ["clone", "--branch", config.Branch, "--single-branch", "--depth", cloneDepth, remoteUrl, request.WorkDir],
                gitEnv,
                cancellationToken);

            if (!cloneResult.Succeeded)
            {
                logger.LogWarning("Branch '{Branch}' not found on remote yet; cloning default branch and creating it.", config.Branch);
                await RunGitOrThrowAsync(null, ["clone", "--depth", cloneDepth, remoteUrl, request.WorkDir], gitEnv, cancellationToken);
                await RunGitOrThrowAsync(request.WorkDir, ["checkout", "-B", config.Branch], gitEnv, cancellationToken);
            }

            await RunGitOrThrowAsync(request.WorkDir, ["config", "user.name", config.CommitUserName], gitEnv, cancellationToken);
            await RunGitOrThrowAsync(request.WorkDir, ["config", "user.email", config.CommitUserEmail], gitEnv, cancellationToken);

            string targetDir = Path.Combine(request.WorkDir, config.PathPrefix);
            // Wipe and repopulate so objects dropped from the source database (or excluded by an updated
            // filter) show up as deletions in git, rather than lingering forever.
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
            Directory.CreateDirectory(targetDir);

            if (Directory.Exists(request.StagingRoot))
            {
                CopyDirectoryContents(request.StagingRoot, targetDir);
            }

            if (request.PostSyncHookAsync is not null)
            {
                logger.LogInformation("Running post-sync hook before commit");
                await request.PostSyncHookAsync(request.WorkDir, config.PathPrefix, cancellationToken);
            }

            await RunGitOrThrowAsync(request.WorkDir, ["add", "-A"], gitEnv, cancellationToken);
            ProcessResult status = await RunGitOrThrowAsync(request.WorkDir, ["status", "--porcelain"], gitEnv, cancellationToken);

            if (string.IsNullOrWhiteSpace(status.StandardOutput))
            {
                logger.LogInformation("No changes detected; nothing to publish.");
                return new GitPublishResult { Published = false };
            }

            string fullMessage = string.IsNullOrEmpty(request.Summary) ? config.CommitMessage : $"{config.CommitMessage}\n\n{request.Summary}";
            await RunGitOrThrowAsync(request.WorkDir, ["commit", "-m", fullMessage], gitEnv, cancellationToken);

            logger.LogInformation("Pushing to '{Branch}'", config.Branch);
            await RunGitOrThrowAsync(request.WorkDir, ["push", "origin", $"HEAD:{config.Branch}"], gitEnv, cancellationToken);

            return new GitPublishResult { Published = true };
        }
        finally
        {
            try
            {
                File.Delete(askPassPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task<ProcessResult> RunGitAsync(string? workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environmentVariables, CancellationToken cancellationToken) =>
        await processRunner.RunAsync("git", arguments, workingDirectory, environmentVariables, cancellationToken);

    private async Task<ProcessResult> RunGitOrThrowAsync(string? workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environmentVariables, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitAsync(workingDirectory, arguments, environmentVariables, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed (exit {result.ExitCode}):\n{result.StandardError}{result.StandardOutput}");
        }

        return result;
    }

    /// <summary>A direct port of Get-SyncSqlTargetRepoUrl: falls back to deriving the repo URL from GitLab CI predefined variables when config.git.remoteUrl isn't set.</summary>
    private static string ResolveRemoteUrl(string? configRemoteUrl)
    {
        if (!string.IsNullOrWhiteSpace(configRemoteUrl))
        {
            return configRemoteUrl;
        }

        string? protocol = Environment.GetEnvironmentVariable("CI_SERVER_PROTOCOL");
        string? host = Environment.GetEnvironmentVariable("CI_SERVER_HOST");
        string? projectPath = Environment.GetEnvironmentVariable("CI_PROJECT_PATH");
        if (string.IsNullOrWhiteSpace(protocol) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(projectPath))
        {
            throw new InvalidOperationException(
                "git.remoteUrl is not set in the config and CI_SERVER_PROTOCOL/CI_SERVER_HOST/CI_PROJECT_PATH are unavailable; cannot determine the target repository.");
        }

        return $"{protocol}://{host}/{projectPath}.git";
    }

    /// <summary>
    /// Writes a tiny askpass helper that reads the token from this process's own environment variables
    /// (SYNCSQL_GIT_USERNAME/PASSWORD, passed per-invocation to each `git` call - see PublishAsync) rather
    /// than from a command-line argument, so the token never appears in a process listing.
    /// </summary>
    private static string WriteAskPassScript()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"syncsql-askpass-{Environment.ProcessId}.sh");
        const string Content = "#!/bin/sh\ncase \"$1\" in\n    Username*) printf '%s' \"$SYNCSQL_GIT_USERNAME\" ;;\n    *)         printf '%s' \"$SYNCSQL_GIT_PASSWORD\" ;;\nesac\n";
        File.WriteAllText(scriptPath, Content);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return scriptPath;
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            File.Copy(filePath, Path.Combine(destinationDir, Path.GetFileName(filePath)), overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryContents(subDir, destSubDir);
        }
    }
}
