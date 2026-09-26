using Microsoft.Extensions.Logging;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class SynonymSyntaxTests
{
    [Theory]
    [InlineData("CREATE OR REPLACE EDITIONABLE SYNONYM reporting.current_orders FOR app.orders;", "app", "orders", null)]
    [InlineData("CREATE OR REPLACE NONEDITIONABLE SYNONYM reporting.current_orders FOR app.orders@sales.world;", "app", "orders", "sales.world")]
    [InlineData("CREATE NONEDITIONABLE PUBLIC SYNONYM current_orders FOR app.orders@sales.world;", "app", "orders", "sales.world")]
    [InlineData("CREATE EDITIONABLE PUBLIC SYNONYM current_orders FOR orders;", null, "orders", null)]
    [InlineData("CREATE SYNONYM reporting.current_orders FOR orders;", null, "orders", null)]
    [InlineData("CREATE SYNONYM current_orders FOR app.orders;", "app", "orders", null)]
    [InlineData("CREATE SYNONYM current_orders FOR orders@sales.world;", null, "orders", "sales.world")]
    [InlineData("CREATE PUBLIC SYNONYM current_orders FOR app.orders;", "app", "orders", null)]
    [InlineData("create or replace /* edition */ editionable synonym \"Reporting\".\"Alias\" for \"App\".\"Order Items\";", "App", "Order Items", null)]
    public void Synonym_ParsesWithoutWarningsAndReferencesOnlyItsTarget(string sql, string? owner, string name, string? link)
    {
        RecordingLogger logger = new();
        var result = new OracleLineageAnalyzer(logger).Analyze(sql);
        Assert.Equal(new ObjectRef(owner, name) { Server = link }, Assert.Single(result.ObjectRefs));
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Synonym_MissingTargetStillReportsSyntaxError()
    {
        RecordingLogger logger = new();
        new OracleLineageAnalyzer(logger).Analyze("CREATE OR REPLACE EDITIONABLE SYNONYM app.broken FOR;");
        Assert.NotEmpty(logger.Warnings);
    }

    private sealed class RecordingLogger : ILogger<OracleLineageAnalyzer>
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
