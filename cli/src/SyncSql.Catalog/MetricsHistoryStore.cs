using System.Text.Json;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Catalog;

/// <summary>
/// Folds each run's freshly captured metrics snapshots into a growing per-object history array, kept
/// entirely separate from the object's own versioned DDL file. A direct port of
/// Update-MetricsHistory.ps1. Objects dropped from the source database keep their existing history file
/// (not cleaned up here) - a minor storage cost, not a correctness issue, and avoids needing to
/// cross-reference the full current object list.
/// </summary>
public sealed class MetricsHistoryStore(ILogger<MetricsHistoryStore> logger) : IMetricsHistoryStore
{
    public async Task<int> UpdateAsync(MetricsHistoryUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.SnapshotRoot))
        {
            logger.LogWarning("Snapshot root '{SnapshotRoot}' not found; nothing to update.", request.SnapshotRoot);
            return 0;
        }

        string[] files = Directory.GetFiles(request.SnapshotRoot, "*.json", SearchOption.AllDirectories);
        logger.LogInformation(
            "Updating metrics history for {Count} object(s) under {HistoryRoot} (retaining up to {Limit} snapshot(s) each)",
            files.Length, request.HistoryRoot, request.HistoryLimit);

        Directory.CreateDirectory(request.HistoryRoot);
        int updated = 0;

        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(request.SnapshotRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            string historyPath = Path.Combine(request.HistoryRoot, relative);

            List<MetricsSnapshot> existing = [];
            if (File.Exists(historyPath))
            {
                try
                {
                    string raw = await File.ReadAllTextAsync(historyPath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        existing = JsonSerializer.Deserialize<List<MetricsSnapshot>>(raw, SyncSqlJsonOptions.Default) ?? [];
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning("Existing metrics history at '{Path}' could not be parsed (starting fresh): {Message}", historyPath, ex.Message);
                }
            }

            MetricsSnapshot newSnapshot;
            try
            {
                string raw = await File.ReadAllTextAsync(file, cancellationToken);
                newSnapshot = JsonSerializer.Deserialize<MetricsSnapshot>(raw, SyncSqlJsonOptions.Default)
                    ?? throw new JsonException("snapshot file deserialized to null");
            }
            catch (JsonException ex)
            {
                logger.LogWarning("Snapshot file '{Path}' could not be parsed (skipping): {Message}", file, ex.Message);
                continue;
            }

            existing.Add(newSnapshot);
            List<MetricsSnapshot> trimmed = existing.Count > request.HistoryLimit
                ? existing[^request.HistoryLimit..]
                : existing;

            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(trimmed, SyncSqlJsonOptions.Default), cancellationToken);
            updated++;
        }

        logger.LogInformation("Metrics history updated for {Count} object(s) under {HistoryRoot}", updated, request.HistoryRoot);
        return updated;
    }

    public async Task<IReadOnlyList<MetricsSnapshot>> LoadHistoryAsync(string historyRoot, string objectId, CancellationToken cancellationToken)
    {
        string path = Path.Combine(historyRoot, objectId.Replace('/', Path.DirectorySeparatorChar) + ".json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            string raw = await File.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<MetricsSnapshot>>(raw, SyncSqlJsonOptions.Default) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Metrics history at '{Path}' could not be parsed (leaving node.metrics empty): {Message}", path, ex.Message);
            return [];
        }
    }
}
