using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;
using SyncSql.Core.Serialization;

namespace SyncSql.Cli.Sync;

/// <summary>
/// Writes one server extraction's outcome to disk: each object as its own .sql file under the staging
/// tree (<see cref="ExtractedObjectFile"/>'s format) and each metrics snapshot as its own JSON file under
/// the metrics root, at the object's id - the shape <see cref="Core.Abstractions.IMetricsHistoryStore"/>
/// expects for -SnapshotRoot. Kept out of SyncCommand's own action delegate so it's a plain, directly
/// testable function of (outcome, roots) -&gt; files-on-disk.
/// </summary>
internal static class ExtractionOutputWriter
{
    public static async Task WriteAsync(ExtractionOutcome outcome, string stagingRoot, string metricsRoot, CancellationToken cancellationToken)
    {
        foreach (ExtractedObject obj in outcome.Objects)
        {
            string relativePath = ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name);
            string path = Path.Combine(stagingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, ExtractedObjectFile.Write(obj), cancellationToken);
        }

        foreach ((string objectId, MetricsSnapshot snapshot) in outcome.MetricsSnapshots)
        {
            string path = Path.Combine(metricsRoot, objectId.Replace('/', Path.DirectorySeparatorChar) + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, SyncSqlJsonOptions.Default), cancellationToken);
        }
    }
}
