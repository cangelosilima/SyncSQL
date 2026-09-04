using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>
/// The default layout every command reads and writes, so running <c>syncsql</c> away from a CI job needs
/// no path parameter at all: everything lands under <c>./syncsql-output</c> in the current directory
/// (overridable wholesale with <c>--output-root</c>, or one path at a time with the specific option).
/// A pipeline still pins every path explicitly - see .gitlab/README.md - it just no longer has to.
///
/// The extracted objects are the output root's own contents rather than a folder inside it: the first
/// path segment of every extracted file is the server it came from. The two sibling folders that are not
/// part of that tree (<c>metrics-snapshot/</c>, <c>metrics/</c>) hold JSON only, so a catalog build or a
/// lint pass scanning the root for <c>*.sql</c> never picks them up.
/// </summary>
internal static class SyncSqlPaths
{
    public const string DefaultOutputRoot = "syncsql-output";
    public const string DefaultConfigPath = "config/servers.json";

    /// <summary>
    /// The extracted tree starts at the server name, directly under the output root - there is no wrapping
    /// folder, so an object lands at <c>&lt;output-root&gt;/SQLPROD01/AppDb/Tables/dbo/Orders.sql</c>. The
    /// git side matches: <see cref="DefaultPathPrefix"/> puts the same tree at the repository root.
    /// </summary>
    public const string ObjectsRelativePath = "";

    /// <summary>Where the extracted tree sits inside the publish repository: at its root, so the first path segment is the server name.</summary>
    public const string DefaultPathPrefix = "";

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
