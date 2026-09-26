using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class LegacySyntaxTests
{
    [Theory]
    [InlineData("RAISERROR @errno @errmsg")]
    [InlineData("RAISERROR 50001 'legacy error'")]
    public void LegacyRaiseError_PreservesReferencesAcrossTheStatement(string statement)
    {
        string sql = $"CREATE PROCEDURE dbo.Legacy AS BEGIN SELECT id FROM dbo.BeforeError; {statement}; SELECT id FROM dbo.AfterError; END";
        TSqlParserFactory.GetParser().Parse(new StringReader(sql), out IList<ParseError> errors);
        Assert.NotEmpty(errors);
        RecordingLogger logger = new();

        var result = new MsSqlLineageAnalyzer(logger).Analyze(sql);

        Assert.Contains(result.ObjectRefs, r => r.Schema == "dbo" && r.Name == "BeforeError");
        Assert.Contains(result.ObjectRefs, r => r.Schema == "dbo" && r.Name == "AfterError");
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void FailedLegacyRetry_PreservesModernPartialAstAndReportsPosition()
    {
        const string sql = "SELECT JSON_VALUE(payload, '$.id') FROM dbo.Modern;\nSELECT (";
        RecordingLogger logger = new();

        var result = new MsSqlLineageAnalyzer(logger).Analyze(sql);

        Assert.Contains(result.ObjectRefs, r => r.Schema == "dbo" && r.Name == "Modern");
        Assert.Contains("line 2, column", Assert.Single(logger.Warnings));
    }

    [Fact]
    public void ModernSql_StillUsesModernGrammar()
    {
        RecordingLogger logger = new();
        var result = new MsSqlLineageAnalyzer(logger).Analyze(
            "CREATE OR ALTER PROCEDURE dbo.Modern AS SELECT JSON_VALUE(payload, '$.id') FROM dbo.Source;");
        Assert.Contains(result.ObjectRefs, r => r.Schema == "dbo" && r.Name == "Source");
        Assert.Empty(logger.Warnings);
    }

    private sealed class RecordingLogger : ILogger<MsSqlLineageAnalyzer>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) { Warnings.Add(formatter(state, exception)); }
        }
    }
}
