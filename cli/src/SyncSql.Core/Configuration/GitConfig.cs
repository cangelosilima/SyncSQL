using System.Text.Json.Serialization;

namespace SyncSql.Core.Configuration;

/// <summary>
/// config.git - where extracted objects get pushed. All fields optional; the CLI never resolves these to
/// defaults or acts on them itself (see .gitlab/ci/sync.yml, which reads this block directly and
/// runs the actual clone/commit/push) - this record exists purely so `validate-config` can parse and
/// validate the block's shape as part of the config schema.
/// </summary>
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
}
