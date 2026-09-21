using Microsoft.Extensions.Logging;

namespace SyncSql.Cli.Tests;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public async Task FileLogger_WritesConcurrentMessagesAndExceptionsWithoutTerminalEscapes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"syncsql-log-{Guid.NewGuid():N}.log");
        try
        {
            using SyncSqlFileLoggerProvider provider = new(path);
            ILogger logger = provider.CreateLogger("test");
            using IDisposable? scope = logger.BeginScope("operation");
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.False(logger.IsEnabled(LogLevel.None));
            await Parallel.ForEachAsync(Enumerable.Range(0, 100), (index, _) =>
            {
                logger.LogInformation("Message {Index}", index);
                return ValueTask.CompletedTask;
            });
            logger.LogError(new InvalidOperationException("database details"), "Extraction failed");
            provider.Dispose();
            logger.LogInformation("Late message");

            string[] lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(100, lines.Count(line => line.StartsWith("[INFO]", StringComparison.Ordinal)));
            Assert.Equal(100, lines.Where(line => line.StartsWith("[INFO]", StringComparison.Ordinal)).Distinct().Count());
            string log = string.Join('\n', lines);
            Assert.Contains("[ERROR] Extraction failed", log);
            Assert.Contains("InvalidOperationException: database details", log);
            Assert.DoesNotContain("Late message", log);
            Assert.DoesNotContain('\e', log);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
