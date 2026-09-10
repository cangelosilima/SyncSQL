using System.Text;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// Assertions here collect every problem before failing, rather than stopping at the first one: with
/// twenty databases in play, "eleven objects are missing and here they are" is a far more useful
/// failure than "SAMPLES-ORACLE/FREEPDB1/Tables/HR/COUNTRIES is missing".
/// </summary>
internal static class BenchmarkReport
{
    private const int MaxListed = 40;

    public static string Describe(string headline, IReadOnlyList<string> problems)
    {
        StringBuilder builder = new();
        builder.AppendLine($"{headline} ({problems.Count}):");
        foreach (string problem in problems.Take(MaxListed))
        {
            builder.AppendLine($"  - {problem}");
        }
        if (problems.Count > MaxListed)
        {
            builder.AppendLine($"  ... and {problems.Count - MaxListed} more");
        }
        return builder.ToString();
    }
}
