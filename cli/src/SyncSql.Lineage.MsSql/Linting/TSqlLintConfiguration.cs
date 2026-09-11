using System.Text.Json;

namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>The lint section of the shared config/sql-style.json document.</summary>
public sealed record TSqlLintConfiguration(TSqlLintSeverity FailOn, IReadOnlyDictionary<string, TSqlLintSeverity?> Rules)
{
    public static TSqlLintConfiguration Default { get; } = LoadDefault();

    public static TSqlLintConfiguration Load(string path) => Parse(File.ReadAllText(path));

    public static TSqlLintConfiguration Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out JsonElement version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int versionNumber) || versionNumber != 1)
        {
            throw new InvalidDataException("SQL style configuration requires version 1.");
        }

        if (!root.TryGetProperty("lint", out JsonElement lint) || lint.ValueKind != JsonValueKind.Object ||
            !lint.TryGetProperty("failOn", out JsonElement failOn) || failOn.ValueKind != JsonValueKind.String ||
            !lint.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("SQL style configuration requires lint.failOn and lint.rules.");
        }

        TSqlLintSeverity threshold = ParseSeverity(failOn.GetString(), "lint.failOn")
            ?? throw new InvalidDataException("lint.failOn must be 'warning' or 'error'.");
        Dictionary<string, TSqlLintSeverity?> configured = new(StringComparer.Ordinal);
        foreach (JsonProperty rule in rules.EnumerateObject())
        {
            if (rule.Name is not ("syntax-error" or "select-star" or "nolock-hint" or "cursor-usage"))
            {
                throw new InvalidDataException($"Unknown SQL lint rule '{rule.Name}'.");
            }

            if (rule.Value.ValueKind != JsonValueKind.String || !configured.TryAdd(rule.Name, ParseSeverity(rule.Value.GetString(), rule.Name)))
            {
                throw new InvalidDataException($"Invalid or duplicate SQL lint rule '{rule.Name}'.");
            }
        }

        if (configured.Count != 4)
        {
            throw new InvalidDataException("lint.rules must configure syntax-error, select-star, nolock-hint and cursor-usage.");
        }

        return new TSqlLintConfiguration(threshold, configured);
    }

    private static TSqlLintSeverity? ParseSeverity(string? value, string field) => value switch
    {
        "warning" => TSqlLintSeverity.Warning,
        "error" => TSqlLintSeverity.Error,
        "off" => null,
        _ => throw new InvalidDataException($"Invalid {field} severity '{value}': expected 'warning', 'error' or 'off'."),
    };

    private static TSqlLintConfiguration LoadDefault() =>
        LoadDefault(typeof(TSqlLintConfiguration).Assembly.GetManifestResourceStream("SyncSql.SqlStyle.json"));

    internal static TSqlLintConfiguration LoadDefault(Stream? resource)
    {
        using Stream stream = resource
            ?? throw new InvalidOperationException("The packaged SQL style configuration is missing.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }
}
