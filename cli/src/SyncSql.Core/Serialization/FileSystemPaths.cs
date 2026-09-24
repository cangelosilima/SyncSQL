namespace SyncSql.Core.Serialization;

/// <summary>Filesystem path comparisons, kept separate from case-insensitive database identifiers.</summary>
public static class FileSystemPaths
{
    public static StringComparison Comparison { get; } = ComparisonFor(OperatingSystem.IsWindows());
    public static StringComparer Comparer { get; } = StringComparer.FromComparison(Comparison);

    internal static StringComparison ComparisonFor(bool windows) => windows
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
