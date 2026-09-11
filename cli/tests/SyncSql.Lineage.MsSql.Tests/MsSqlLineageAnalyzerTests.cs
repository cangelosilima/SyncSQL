using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
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

    [Fact]
    public void Analyze_ThreePartName_KeepsTheDatabaseQualifier()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[SyncOrders] AS
            BEGIN
                SELECT * FROM [SalesDb].[dbo].[Orders];
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Server: null, Database: "SalesDb", Schema: "dbo", Name: "Orders" });
    }

    [Fact]
    public void Analyze_FourPartName_KeepsTheLinkedServerAndDatabaseQualifiers()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[SyncOrders] AS
            BEGIN
                SELECT * FROM [SALES_LINK].[SalesDb].[dbo].[Orders] o WHERE o.Total > 0;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Server: "SALES_LINK", Database: "SalesDb", Schema: "dbo", Name: "Orders" });
        Assert.Equal("SALES_LINK", result.Aliases["o"].Server);
    }

    [Fact]
    public void Analyze_FourPartNameWithAnEmptyDatabasePart_LeavesTheDatabaseUnset()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[SyncOrders] AS
            BEGIN
                SELECT * FROM SALES_LINK..dbo.Orders;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Server: "SALES_LINK", Database: null, Schema: "dbo", Name: "Orders" });
    }

    [Fact]
    public void Analyze_CrossDatabaseFunctionCall_KeepsTheDatabaseQualifier()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[DoStuff] AS
            BEGIN
                SELECT [SalesDb].[dbo].[CalculateTotal](1);
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Database: "SalesDb", Schema: "dbo", Name: "CalculateTotal" });
    }

    [Fact]
    public void Analyze_UnqualifiedReference_LeavesDatabaseAndServerUnset()
    {
        LineageAnalysisResult result = _analyzer.Analyze("CREATE VIEW dbo.V AS SELECT * FROM dbo.Orders;");

        Assert.Contains(result.ObjectRefs, r => r is { Server: null, Database: null, Schema: "dbo", Name: "Orders" });
    }

    [Fact]
    public void Analyze_TempTables_AreNotReferencesAtAll()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[Stage] AS
            BEGIN
                SELECT * INTO #Staging FROM [dbo].[Orders];
                SELECT * FROM #Staging JOIN ##Shared ON 1 = 1;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name.StartsWith('#'));
    }

    [Fact]
    public void Analyze_CommonTableExpressionName_IsNotTreatedAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE VIEW [dbo].[RecentOrders] AS
            WITH Recent AS (SELECT * FROM [dbo].[Orders])
            SELECT * FROM Recent;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "Recent");
    }

    /// <summary>
    /// The scenario this whole dynamic-SQL path exists for: the object reaches a remote function through
    /// OPENQUERY, inside a string built by concatenation, and nothing about that is visible on the parse
    /// tree. The linked server has to come out attached to the reference, or SyncSql.Catalog cannot draw
    /// the hop.
    /// </summary>
    [Fact]
    public void Analyze_OpenQueryInsideABuiltString_FindsTheRemoteFunctionOnItsLinkedServer()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE FUNCTION dbo.fns_GetOrderId(@Id_Order CHAR(13))
            RETURNS VARCHAR(50)
            AS
            BEGIN
                DECLARE @Result TABLE (OrderId VARCHAR(50));
                DECLARE @SQL NVARCHAR(MAX) = 'SELECT OrderId FROM OPENQUERY(SQL_A, ''SELECT dbo.fns_GetOrderId(''' + @Id_Order + ''') AS OrderId'')';
                RETURN (SELECT TOP 1 OrderId FROM @Result);
            END
            """);

        Assert.Contains(
            result.ObjectRefs,
            r => r is { Server: "SQL_A", Schema: "dbo", Name: "fns_GetOrderId", Origin: ReferenceOrigin.Dynamic });
    }

    [Fact]
    public void Analyze_ExecOfAStringLiteral_FindsTheReferenceAndMarksItDynamic()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[Rebuild] AS
            BEGIN
                EXEC ('SELECT * FROM dbo.Orders');
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders", Origin: ReferenceOrigin.Dynamic });
    }

    [Fact]
    public void Analyze_ExecAtLinkedServer_AttributesTheReferenceToThatServer()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[Remote] AS
            BEGIN
                EXEC ('SELECT * FROM dbo.Orders') AT SALES_LINK;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Server: "SALES_LINK", Schema: "dbo", Name: "Orders" });
    }

    [Fact]
    public void Analyze_StaticReference_IsNotMarkedDynamic()
    {
        LineageAnalysisResult result = _analyzer.Analyze("CREATE VIEW dbo.V AS SELECT * FROM dbo.Orders;");

        Assert.Contains(result.ObjectRefs, r => r is { Name: "Orders", Origin: ReferenceOrigin.Static });
    }

    [Fact]
    public void Analyze_WithDynamicSqlDisabled_LeavesBuiltStringsAlone()
    {
        LineageAnalysisResult result = _analyzer.Analyze(
            """
            CREATE PROCEDURE [dbo].[Rebuild] AS
            BEGIN
                EXEC ('SELECT * FROM dbo.Orders');
            END
            """,
            new LineageAnalysisOptions { DynamicSql = false });

        Assert.Empty(result.ObjectRefs);
    }

    /// <summary>
    /// The dynamic scanner only collects a *qualified* name in a reference position, so a dynamically
    /// built SELECT list full of "alias.column" does not turn every alias into a phantom schema.
    /// </summary>
    [Fact]
    public void Analyze_AliasDotColumnInsideBuiltSql_IsNotMistakenForAnObject()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[Report] AS
            BEGIN
                EXEC ('SELECT o.OrderId, o.Total FROM dbo.Orders o');
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Schema == "o");
    }

    /// <summary>
    /// The reported false positive: T-SQL's multi-table DELETE points at one of the FROM clause's aliases,
    /// which ScriptDom reports as a table named "a". Ordinary, correct SQL used to produce a permanent
    /// orphaned reference for it.
    /// </summary>
    [Fact]
    public void Analyze_DeleteTargetingAFromClauseAlias_DoesNotReportTheAliasAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze(
            "DELETE a FROM table_a AS a JOIN table_b AS b ON b.id = a.id;");

        Assert.Contains(result.ObjectRefs, r => r is { Schema: null, Name: "table_a" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: null, Name: "table_b" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "a" or "b");
    }

    [Fact]
    public void Analyze_UpdateTargetingAFromClauseAlias_DoesNotReportTheAliasAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE [dbo].[SyncTotals] AS
            BEGIN
                UPDATE a
                SET a.Total = b.Total
                FROM [dbo].[Orders] AS a
                JOIN [dbo].[OrderStaging] AS b ON b.OrderId = a.OrderId;
            END
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "OrderStaging" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "a" or "b");
    }

    [Fact]
    public void Analyze_DeleteTargetingAnAliasThroughAParenthesizedJoin_DoesNotReportTheAlias()
    {
        LineageAnalysisResult result = _analyzer.Analyze(
            "DELETE a FROM (dbo.table_a AS a JOIN dbo.table_b AS b ON b.id = a.id);");

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "table_a" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "a");
    }

    /// <summary>
    /// The skip is by alias, not by "the target looks unqualified": a plain DELETE names the table itself
    /// and has to stay a reference.
    /// </summary>
    [Fact]
    public void Analyze_DeleteWithoutASecondFromClause_StillReferencesItsTarget()
    {
        LineageAnalysisResult result = _analyzer.Analyze("DELETE FROM dbo.table_a WHERE id = 1;");

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "table_a" });
    }

    [Fact]
    public void Analyze_DeleteNamingItsTargetInFull_StillReferencesIt()
    {
        LineageAnalysisResult result = _analyzer.Analyze(
            "DELETE dbo.table_a FROM dbo.table_a AS a JOIN dbo.table_b AS b ON b.id = a.id;");

        Assert.Equal(2, result.ObjectRefs.Count(r => r is { Schema: "dbo", Name: "table_a" }));
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "table_b" });
    }

    /// <summary>
    /// Why the target is matched by fragment identity rather than by name: an alias may shadow the very
    /// table name it stands for, and the FROM clause's own reference to that table is genuine.
    /// </summary>
    [Fact]
    public void Analyze_AliasShadowingItsOwnTableName_KeepsTheFromClauseReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze(
            "DELETE a FROM a AS a JOIN table_b AS b ON b.id = a.id;");

        Assert.Equal(1, result.ObjectRefs.Count(r => r is { Schema: null, Name: "a" }));
    }
}
