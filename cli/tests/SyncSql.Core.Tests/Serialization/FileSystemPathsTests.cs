using SyncSql.Core.Serialization;

namespace SyncSql.Core.Tests.Serialization;

public class FileSystemPathsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PathsUseOrdinalComparisonWithPlatformCaseRules(bool windows)
    {
        var comparer = StringComparer.FromComparison(FileSystemPaths.ComparisonFor(windows));
        Assert.Equal(windows, comparer.Equals("/exports/DB", "/exports/db"));
        Assert.True(comparer.Equals("/exports/DB", "/exports/DB"));
        Assert.False(comparer.Equals("/exports/café", "/exports/cafe\u0301"));
        Assert.Equal(FileSystemPaths.ComparisonFor(OperatingSystem.IsWindows()), FileSystemPaths.Comparison);
        Assert.Equal(OperatingSystem.IsWindows(), FileSystemPaths.Comparer.Equals("DB", "db"));
    }
}
