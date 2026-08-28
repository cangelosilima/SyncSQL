using SyncSql.Lineage.MsSql.Linting;

namespace SyncSql.Lineage.MsSql.Tests.Linting;

public class TSqlLinterTests
{
    private readonly TSqlLinter _linter = new();

    [Fact]
    public void Lint_SelectStar_FlagsSelectStarRule()
    {
        IReadOnlyList<TSqlLintFinding> findings = _linter.Lint("""
            CREATE VIEW [dbo].[OrderSummary] AS
            SELECT * FROM [dbo].[Orders]
            """);

        Assert.Contains(findings, f => f is { RuleId: "select-star", Severity: TSqlLintSeverity.Warning });
    }

    [Fact]
    public void Lint_NoLockHint_FlagsNoLockHintRule()
    {
        IReadOnlyList<TSqlLintFinding> findings = _linter.Lint("""
            CREATE PROCEDURE [dbo].[GetOrders] AS
            BEGIN
                SELECT [OrderId] FROM [dbo].[Orders] WITH (NOLOCK)
            END
            """);

        Assert.Contains(findings, f => f is { RuleId: "nolock-hint", Severity: TSqlLintSeverity.Warning });
    }

    [Fact]
    public void Lint_DeclareCursor_FlagsCursorUsageRule()
    {
        IReadOnlyList<TSqlLintFinding> findings = _linter.Lint("""
            CREATE PROCEDURE [dbo].[WalkOrders] AS
            BEGIN
                DECLARE order_cursor CURSOR FOR SELECT [OrderId] FROM [dbo].[Orders];
            END
            """);

        Assert.Contains(findings, f => f is { RuleId: "cursor-usage", Severity: TSqlLintSeverity.Warning });
    }

    [Fact]
    public void Lint_InvalidSyntax_ReportsSyntaxErrorFinding()
    {
        IReadOnlyList<TSqlLintFinding> findings = _linter.Lint("SELECT FROM WHERE (((");

        Assert.Contains(findings, f => f is { RuleId: "syntax-error", Severity: TSqlLintSeverity.Error });
    }

    [Fact]
    public void Lint_CleanScript_ReturnsNoFindings()
    {
        IReadOnlyList<TSqlLintFinding> findings = _linter.Lint("""
            CREATE VIEW [dbo].[OrderSummary] AS
            SELECT [OrderId], [CustomerId] FROM [dbo].[Orders]
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Lint_EmptyOrWhitespaceSql_ReturnsNoFindings()
    {
        Assert.Empty(_linter.Lint(""));
        Assert.Empty(_linter.Lint("   "));
    }
}
