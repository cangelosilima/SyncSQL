using SyncSql.Lineage.MsSql.Linting;

namespace SyncSql.Lineage.MsSql.Tests.Linting;

public class TSqlLintConfigurationTests
{
    private const string Configuration = """
        { "version": 1, "lint": { "failOn": "warning", "rules": {
          "syntax-error": "error", "select-star": "off", "nolock-hint": "error", "cursor-usage": "warning"
        } } }
        """;

    [Fact]
    public void Default_LoadsPackagedJson()
    {
        Assert.Equal(TSqlLintSeverity.Error, TSqlLintConfiguration.Default.FailOn);
        Assert.Equal(TSqlLintSeverity.Warning, TSqlLintConfiguration.Default.Rules["select-star"]);
        Assert.Equal(4, TSqlLintConfiguration.Default.Rules.Count);
    }

    [Fact]
    public void ConfiguredRules_DisableAndOverrideSeverity()
    {
        TSqlLintConfiguration configuration = TSqlLintConfiguration.Parse(Configuration);
        Assert.Equal(TSqlLintSeverity.Warning, configuration.FailOn);
        TSqlLinter linter = new(configuration);
        IReadOnlyList<TSqlLintFinding> findings = linter.Lint("SELECT * FROM dbo.Items WITH (NOLOCK);");
        Assert.DoesNotContain(findings, f => f.RuleId == "select-star");
        Assert.Contains(findings, f => f is { RuleId: "nolock-hint", Severity: TSqlLintSeverity.Error });
        Assert.Contains(linter.Lint("SELECT FROM WHERE ((("), f => f is { RuleId: "syntax-error", Severity: TSqlLintSeverity.Error });
    }

    [Theory]
    [InlineData("\"version\": 1", "\"version\": 2")]
    [InlineData("\"failOn\": \"warning\"", "\"failOn\": \"off\"")]
    [InlineData("\"select-star\"", "\"unknown-rule\"")]
    [InlineData("\"off\"", "\"invalid\"")]
    public void InvalidConfiguration_IsRejected(string before, string after)
    {
        Assert.Throws<InvalidDataException>(() => TSqlLintConfiguration.Parse(Configuration.Replace(before, after)));
    }
}
