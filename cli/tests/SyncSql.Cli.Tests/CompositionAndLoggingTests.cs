using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Cli.Composition;
using SyncSql.Core.Domain;

namespace SyncSql.Cli.Tests;

public sealed class CompositionAndLoggingTests
{
    [Fact]
    public void Resolvers_SelectEngineAndRejectUnregisteredEngines()
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        ServiceCollectionExtensions.AddSyncSqlServices(registrations);
        using var services = registrations.BuildServiceProvider();
        var extraction = new DatabaseObjectExtractorResolver(services);
        var lineage = new LineageAnalyzerResolver(services);
        foreach (DatabaseEngine engine in Enum.GetValues<DatabaseEngine>())
        {
            Assert.Equal(engine, extraction.Resolve(engine).Engine);
            Assert.Equal(engine, lineage.Resolve(engine).Engine);
        }
        Assert.Throws<InvalidOperationException>(() => extraction.Resolve((DatabaseEngine)999));
        Assert.Throws<InvalidOperationException>(() => lineage.Resolve((DatabaseEngine)999));
    }

    [Theory]
    [InlineData(LogLevel.Critical, "[ERROR]")]
    [InlineData(LogLevel.Error, "[ERROR]")]
    [InlineData(LogLevel.Warning, "[WARN]")]
    [InlineData(LogLevel.Debug, "[DEBUG]")]
    [InlineData(LogLevel.Trace, "[DEBUG]")]
    [InlineData(LogLevel.Information, "[INFO]")]
    [InlineData(LogLevel.None, "[INFO]")]
    public void Formatter_LabelsAllLevels(LogLevel level, string label)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        LogEntry<string> entry = new(level, "test", 0, "message", null, (state, _) => state);
        new SyncSqlConsoleFormatter().Write(entry, null, writer);
        Assert.Contains(label, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("message", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Formatter_SuppressesEmptyMessagesButKeepsExceptions(string? message)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        var formatter = new SyncSqlConsoleFormatter();
        LogEntry<string?> empty = new(LogLevel.Error, "test", 0, message, null, (state, _) => state!);
        formatter.Write(empty, null, writer);
        Assert.Equal("", writer.ToString());
        LogEntry<string?> error = new(LogLevel.Error, "test", 0, message, new InvalidOperationException("details"), (state, _) => state!);
        formatter.Write(error, null, writer);
        Assert.Contains("details", writer.ToString(), StringComparison.Ordinal);
    }
}
