using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Lineage.MsSql;

namespace SyncSql.Lineage.MsSql.Tests;

/// <summary>
/// The exact scenarios hand-verified against the real ScriptDom assembly via mcs/mono before this
/// solution existed (see the session's PowerShell lineage work) - ported here as real, repeatable xUnit
/// tests instead of one-off manual verification.
/// </summary>
public class MsSqlLineageAnalyzerTests
{
    private readonly MsSqlLineageAnalyzer _analyzer = new(NullLogger<MsSqlLineageAnalyzer>.Instance);

    [Fact]
    public void Analyze_ViewWithJoinAndAliases_ResolvesObjectRefsAliasesAndColumnRefs()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE VIEW [dbo].[OrderSummary] AS
            SELECT o.OrderId, c.CustomerName, o.Total
            FROM [dbo].[Orders] o
            JOIN [dbo].[Customers] AS c ON o.CustomerId = c.CustomerId
            WHERE o.Total > 100
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Customers" });

        Assert.Equal("Orders", result.Aliases["o"].Name);
        Assert.Equal("Customers", result.Aliases["c"].Name);

        Assert.Contains(result.ColumnRefs, c => c is { AliasOrTable: "o", Column: "OrderId" });
        Assert.Contains(result.ColumnRefs, c => c is { AliasOrTable: "c", Column: "CustomerName" });
        Assert.Contains(result.ColumnRefs, c => c is { AliasOrTable: "o", Column: "CustomerId" });
    }

    [Fact]
    public void Analyze_TableWithAppendedForeignKeySection_ResolvesReferenceTarget()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE TABLE [dbo].[Orders] (
                [OrderId] INT NOT NULL,
                [CustomerId] INT NOT NULL
            )
            ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([CustomerId]);
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Customers" });
    }

    [Fact]
    public void Analyze_SchemaQualifiedFunctionCall_ResolvesFunctionNameNotCallTargetPrefix()
    {
        // Regression test for the bug found via the mono test harness: CallTarget holds only the
        // qualifying prefix ("dbo"), not the function name - a naive "last identifier" reading
        // previously produced Schema=null Name="dbo" instead of Schema="dbo" Name="MyFunc".
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[DoStuff] AS
            BEGIN
                SELECT [dbo].[MyFunc](1) AS X;
                EXEC [dbo].[OtherProc] @p = 1;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "MyFunc" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "OtherProc" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "dbo");
    }

    [Fact]
    public void Analyze_StringLiteralMentioningAnObjectName_IsNotTreatedAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE VIEW [dbo].[Weird] AS
            SELECT 'this text mentions dbo.Customers but is just a string literal' AS Note
            FROM [dbo].[Orders]
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "Customers");
    }

    [Fact]
    public void Analyze_EmptyOrWhitespaceDdl_ReturnsEmptyResult()
    {
        Assert.Empty(_analyzer.Analyze("").ObjectRefs);
        Assert.Empty(_analyzer.Analyze("   ").ObjectRefs);
    }

    [Fact]
    public void Analyze_UnparseableDdl_DegradesToEmptyResultRatherThanThrowing()
    {
        LineageAnalysisResult result = _analyzer.Analyze("THIS IS NOT VALID T-SQL !!! (((");

        Assert.Empty(result.ObjectRefs);
    }
}
