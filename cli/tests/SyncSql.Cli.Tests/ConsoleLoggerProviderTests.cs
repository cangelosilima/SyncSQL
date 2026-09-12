using System.Globalization;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Sync;

namespace SyncSql.Cli.Tests;

public sealed class ConsoleLoggerProviderTests
{
    [Theory]
    [InlineData(LogLevel.Critical, "[ERROR]")]
    [InlineData(LogLevel.Error, "[ERROR]")]
    [InlineData(LogLevel.Warning, "[WARN]")]
    [InlineData(LogLevel.Debug, "[DEBUG]")]
    [InlineData(LogLevel.Trace, "[DEBUG]")]
    [InlineData(LogLevel.Information, "[INFO]")]
    public void Logger_PreservesSeverityAndExceptionDetails(LogLevel level, string label)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using SyncSqlConsoleLoggerProvider provider = new(new(output, animated: false));
        ILogger logger = provider.CreateLogger("test");
        using IDisposable? scope = logger.BeginScope("operation");
        logger.Log(level, new InvalidOperationException("details"), "message");
        Assert.StartsWith(label, output.ToString());
        Assert.Contains("message", output.ToString());
        Assert.Contains("InvalidOperationException: details", output.ToString());
        Assert.DoesNotContain('\e', output.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Logger_SuppressesEmptyMessagesUnlessAnExceptionIsPresent(string? message)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using SyncSqlConsoleLoggerProvider provider = new(new(output, animated: false));
        ILogger logger = provider.CreateLogger("test");
        logger.Log(LogLevel.None, 0, "ignored", null, (state, _) => state);
        logger.Log(LogLevel.Information, 0, message, null, (state, _) => state!);
        Assert.Equal("", output.ToString());
        logger.Log(LogLevel.Error, 0, message, new InvalidOperationException("details"), (state, _) => state!);
        Assert.Contains("[ERROR]", output.ToString());
        Assert.Contains("details", output.ToString());
    }
}
