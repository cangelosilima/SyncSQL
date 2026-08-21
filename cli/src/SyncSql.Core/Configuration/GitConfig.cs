using System.Text.Json.Serialization;

namespace SyncSql.Core.Configuration;

/// <summary>config.git - where extracted objects get pushed. All fields optional; see <see cref="Resolved"/> for defaults.</summary>
public sealed record GitConfig
{
    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; init; }

    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("pathPrefix")]
    public string? PathPrefix { get; init; }

    [JsonPropertyName("commitUserName")]
    public string? CommitUserName { get; init; }

    [JsonPropertyName("commitUserEmail")]
    public string? CommitUserEmail { get; init; }

    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; init; }

    /// <summary>Resolves every optional key to its default - a direct port of SyncSql.Git.psm1's Get-SyncSqlGitDefaults.</summary>
    public ResolvedGitConfig Resolved() => new()
    {
        RemoteUrl = string.IsNullOrWhiteSpace(RemoteUrl) ? null : RemoteUrl,
        Branch = string.IsNullOrWhiteSpace(Branch) ? "main" : Branch,
        PathPrefix = string.IsNullOrWhiteSpace(PathPrefix) ? "objects" : PathPrefix,
        CommitUserName = string.IsNullOrWhiteSpace(CommitUserName) ? "SyncSQL Bot" : CommitUserName,
        CommitUserEmail = string.IsNullOrWhiteSpace(CommitUserEmail) ? "syncsql-bot@example.com" : CommitUserEmail,
        CommitMessage = string.IsNullOrWhiteSpace(CommitMessage) ? "chore(sync): update database objects" : CommitMessage,
    };
}

/// <summary>config.git with every optional key resolved to its default.</summary>
public sealed record ResolvedGitConfig
{
    public required string? RemoteUrl { get; init; }
    public required string Branch { get; init; }
    public required string PathPrefix { get; init; }
    public required string CommitUserName { get; init; }
    public required string CommitUserEmail { get; init; }
    public required string CommitMessage { get; init; }
}
