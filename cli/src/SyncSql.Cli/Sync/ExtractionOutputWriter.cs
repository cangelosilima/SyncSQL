using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Domain;
using SyncSql.Core.Abstractions;
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
    public static async Task WriteAsync(ExtractionOutcome outcome, string stagingRoot, string metricsRoot, CancellationToken cancellationToken, IReadOnlyList<string>? serverPath = null, IProgress<ExtractionProgress>? progress = null, Core.Configuration.ServerIdentity? serverIdentity = null, ILogger? logger = null)
    {
        int written = 0;
        int total = outcome.Objects.Count + outcome.MetricsSnapshots.Count;
        progress?.Report(new("Writing files", outcome.Objects.Count, written, total));
        foreach (ExtractedObject obj in outcome.Objects)
        {
            string relativePath = ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name, serverPath: serverPath);
            string path = Path.Combine(stagingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string content = ExtractedObjectFile.Write(obj with { ServerIdentity = serverIdentity ?? obj.ServerIdentity });
            string sanitizedContent = ReplaceUnpairedSurrogates(content, out int replacementCount);
            if (replacementCount > 0)
            {
                logger?.LogWarning(
                    "Object {Server}/{Database}/{Schema}/{Type}/{Name} contained {ReplacementCount} invalid UTF-16 surrogate(s); replaced with U+FFFD.",
                    obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name, replacementCount);
            }
            await File.WriteAllTextAsync(path, sanitizedContent, cancellationToken);
            progress?.Report(new("Writing files", outcome.Objects.Count, ++written, total));
        }

        foreach ((string objectId, MetricsSnapshot snapshot) in outcome.MetricsSnapshots)
        {
            string path = Path.Combine(metricsRoot, objectId.Replace('/', Path.DirectorySeparatorChar) + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, SyncSqlJsonOptions.Default), cancellationToken);
            progress?.Report(new("Writing files", outcome.Objects.Count, ++written, total));
        }
    }

    private static string ReplaceUnpairedSurrogates(string value, out int replacementCount)
    {
        replacementCount = 0;
        StringBuilder? sanitized = null;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            bool unpairedHigh = current is >= '\uD800' and <= '\uDBFF'
                && (index + 1 >= value.Length || value[index + 1] is < '\uDC00' or > '\uDFFF');
            bool unpairedLow = current is >= '\uDC00' and <= '\uDFFF'
                && (index == 0 || value[index - 1] is < '\uD800' or > '\uDBFF');
            if (!unpairedHigh && !unpairedLow)
            {
                continue;
            }

            sanitized ??= new StringBuilder(value);
            sanitized[index] = '\uFFFD';
            replacementCount++;
        }

        return sanitized?.ToString() ?? value;
    }
}
