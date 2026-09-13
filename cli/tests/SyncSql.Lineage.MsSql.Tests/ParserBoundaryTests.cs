using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Lineage.MsSql.Linting;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class ParserBoundaryTests
{
    [Fact]
    public async Task Factory_ConcurrentCallsShareTheCachedParser()
    {
        TSqlParser[] parsers = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(TSqlParserFactory.GetParser)));
        Assert.All(parsers, parser => Assert.Same(parsers[0], parser));
    }

    [Fact]
    public void Factory_SelectsNewestParserAndReportsMissingGrammar()
    {
        Assert.IsType<TSql160Parser>(TSqlParserFactory.CreateParser([typeof(string), typeof(TSql150Parser), typeof(TSql160Parser)]));
        Assert.Throws<InvalidOperationException>(() => TSqlParserFactory.CreateParser([]));
        Assert.Same(TSqlParserFactory.GetParser(), TSqlParserFactory.GetParser());
        Assert.Throws<InvalidOperationException>(() => TSqlLintConfiguration.LoadDefault(null));
    }

    [Theory]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"lint\":null}")]
    [InlineData("{\"version\":1,\"lint\":{}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":1}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":\"error\"}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":\"error\",\"rules\":null}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":\"error\",\"rules\":{}}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":\"error\",\"rules\":{\"syntax-error\":1}}}")]
    [InlineData("{\"version\":1,\"lint\":{\"failOn\":\"error\",\"rules\":{\"syntax-error\":\"error\",\"syntax-error\":\"off\"}}}")]
    public void LintConfiguration_RejectsMissingMalformedAndDuplicateFields(string json) =>
        Assert.Throws<InvalidDataException>(() => TSqlLintConfiguration.Parse(json));

    [Fact]
    public void Lint_ReadUncommittedIsReportedWhileReadCommittedIsAllowed()
    {
        var linter = new TSqlLinter();
        Assert.Contains(linter.Lint("SELECT id FROM dbo.t WITH (READUNCOMMITTED);"), f => f.RuleId == "nolock-hint");
        Assert.DoesNotContain(linter.Lint("SELECT id FROM dbo.t WITH (READCOMMITTED);"), f => f.RuleId == "nolock-hint");
    }

    [Theory]
    [InlineData("CREATE OR REPLACE PACKAGE pkg AS END pkg; /")]
    [InlineData("\u0000\u0001")]
    [InlineData("SELECT (")]
    public void Analyzer_InvalidSqlDegradesToPartialOrEmptyResult(string sql)
    {
        var result = new MsSqlLineageAnalyzer(NullLogger<MsSqlLineageAnalyzer>.Instance).Analyze(sql);
        Assert.NotNull(result);
        Assert.Empty(result.ObjectRefs);
    }
}
