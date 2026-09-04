using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>
/// The default layout every command reads and writes, so running <c>syncsql</c> away from a CI job needs
/// no path parameter at all: everything lands under <c>./syncsql-output</c> in the current directory
/// (overridable wholesale with <c>--output-root</c>, or one path at a time with the specific option).
/// A pipeline still pins every path explicitly - see .gitlab/README.md - it just no longer has to.
/// </summary>
internal static class SyncSqlPaths
{
    public const string DefaultOutputRoot = "syncsql-output";
    public const string DefaultConfigPath = "config/servers.json";
    public const string ObjectsDirectoryName = "objects";
    public const string MetricsSnapshotDirectoryName = "metrics-snapshot";
    public const string MetricsHistoryDirectoryName = "metrics";
    public const string CatalogFileName = "catalog.json";

    public static Option<string> OutputRootOption() => new("--output-root")
    {
        Description = "Directory the other output paths default to a folder inside, relative to the current directory unless absolute.",
        DefaultValueFactory = _ => DefaultOutputRoot,
    };

    /// <summary>An explicitly passed path wins; otherwise the documented spot under <paramref name="outputRoot"/>. Always returned absolute, so logs say exactly where output went.</summary>
    public static string Resolve(string? explicitPath, string outputRoot, string defaultRelativePath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(outputRoot, defaultRelativePath)
            : explicitPath);
}
