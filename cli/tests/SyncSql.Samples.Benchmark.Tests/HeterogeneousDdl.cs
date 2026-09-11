using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Samples.Benchmark.Tests;

internal static class HeterogeneousDdl
{
    public static IReadOnlyList<ExtractedObject> LoadObjects() =>
        JsonSerializer.Deserialize<List<ExtractedObject>>(
            File.ReadAllText(Path.Combine(HeterogeneousContract.Root, "objects.json")), SyncSqlJsonOptions.Default)!;
}
