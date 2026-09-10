using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Lineage.MsSql;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class AdvancedSqlLineageTests
{
    private readonly MsSqlLineageAnalyzer _analyzer = new(NullLogger<MsSqlLineageAnalyzer>.Instance);

    [Fact]
    public void Analyze_ParameterizedSpExecuteSql_PreservesDynamicSourceAndStaticExecutor()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            EXEC sys.sp_executesql
                N'SELECT o.OrderId FROM SalesDb.dbo.Orders o WHERE o.CustomerId = @id',
                N'@id int', @id = 42;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Database: "SalesDb", Schema: "dbo", Name: "Orders", Origin: ReferenceOrigin.Dynamic });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "sys", Name: "sp_executesql", Origin: ReferenceOrigin.Static });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Schema == "o");
    }

    [Fact]
    public void Analyze_SetGeneratedSql_RecoversLiteralNamesAroundRuntimePredicate()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            DECLARE @sql nvarchar(max), @id int = 42;
            SET @sql = N'SELECT o.OrderId FROM dbo.Orders o WHERE o.OrderId = '
                + CAST(@id AS nvarchar(12))
                + N' UNION ALL SELECT a.OrderId FROM archive.Orders a';
            EXEC (@sql);
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders", Origin: ReferenceOrigin.Dynamic });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "archive", Name: "Orders", Origin: ReferenceOrigin.Dynamic });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Schema is "o" or "a");
    }

    [Fact]
    public void Analyze_RuntimeOnlyTableName_DoesNotInventItsValue()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE dbo.RunReport @table sysname AS
            BEGIN
                SELECT a.Id FROM dbo.AuditLog a;
                DECLARE @sql nvarchar(max) = N'SELECT * FROM ' + QUOTENAME(@table);
                EXEC (@sql);
            END;
            """);

        Assert.Equal(new ObjectRef("dbo", "AuditLog"), Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_DynamicSqlDisabled_PreservesStaticHalfOfMixedBatch()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT o.OrderId FROM dbo.Orders o;
            EXEC (N'SELECT * FROM archive.Orders');
            """, new LineageAnalysisOptions { DynamicSql = false });

        Assert.Equal(new ObjectRef("dbo", "Orders"), Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_SqlPassedToLoggingProcedure_IsNotExecutedSql()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            EXEC dbo.LogMessage @message = N'SELECT * FROM secret.Payroll';
            """);

        Assert.Equal(new ObjectRef("dbo", "LogMessage"), Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_DynamicSqlComment_DoesNotCreateAPhantomDependency()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            EXEC (N'SELECT o.Id FROM dbo.Orders o /* JOIN secret.Payroll p ON 1 = 1 */');
            """);

        Assert.Equal(new ObjectRef("dbo", "Orders") { Origin = ReferenceOrigin.Dynamic }, Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_SynonymConsumer_PreservesSynonymIdentityForCatalogResolution()
    {
        // The analyzer has no catalog: a synonym must remain a reference to its declared name.
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT s.OrderId FROM reporting.CurrentOrders s;
            """);

        Assert.Equal(new ObjectRef("reporting", "CurrentOrders"), Assert.Single(result.ObjectRefs));
        Assert.Equal(new ObjectRef("reporting", "CurrentOrders"), result.Aliases["s"]);
        Assert.Contains(new ColumnRef("s", "OrderId"), result.ColumnRefs);
    }

    [Fact(Skip = "Known gap: TSqlLineageVisitor does not visit CREATE SYNONYM targets.")]
    public void Analyze_SynonymDefinition_ReferencesCrossDatabaseTarget()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE SYNONYM reporting.CurrentOrders FOR SalesDb.dbo.Orders;
            """);

        Assert.Contains(new ObjectRef("dbo", "Orders") { Database = "SalesDb" }, result.ObjectRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "CurrentOrders");
    }

    [Fact]
    public void Analyze_TempTablePipeline_KeepsPersistentSourceAndDestinationOnly()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE dbo.StageOrders AS
            BEGIN
                SELECT o.OrderId INTO #Stage FROM dbo.Orders o;
                INSERT INTO dbo.OrderArchive (OrderId) SELECT s.OrderId FROM #Stage s;
                DELETE FROM #Stage;
                DROP TABLE #Stage;
            END;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "OrderArchive" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name.StartsWith('#'));
        Assert.DoesNotContain(result.Aliases.Values, r => r.Name.StartsWith('#'));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders;")]
    [InlineData("SELECT o.* FROM dbo.Orders o;")]
    public void Analyze_SelectStar_PreservesObjectWithoutInventingColumnExpansion(string sql)
    {
        // Column expansion needs catalog metadata; the syntax analyzer must not fabricate it.
        LineageAnalysisResult result = _analyzer.Analyze(sql);

        Assert.Equal(new ObjectRef("dbo", "Orders"), Assert.Single(result.ObjectRefs));
        Assert.Empty(result.ColumnRefs);
    }

    [Fact]
    public void Analyze_SelectStarWithExplicitJoinColumns_PreservesKnownColumns()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT o.*, c.Name FROM dbo.Orders o
            JOIN dbo.Customers c ON c.Id = o.CustomerId;
            """);

        Assert.Contains(result.ObjectRefs, r => r.Name == "Orders");
        Assert.Contains(result.ObjectRefs, r => r.Name == "Customers");
        Assert.Contains(new ColumnRef("c", "Name"), result.ColumnRefs);
        Assert.Contains(new ColumnRef("o", "CustomerId"), result.ColumnRefs);
        Assert.DoesNotContain(result.ColumnRefs, c => c.Column == "*");
    }

    [Fact]
    public void Analyze_Merge_RecordsSourceTargetAndJoinColumnsWithoutAliasObjects()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            MERGE dbo.Orders AS target
            USING staging.Orders AS source ON target.OrderId = source.OrderId
            WHEN MATCHED THEN UPDATE SET target.Total = source.Total
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (OrderId, Total) VALUES (source.OrderId, source.Total)
            WHEN NOT MATCHED BY SOURCE THEN DELETE;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "dbo", Name: "Orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "staging", Name: "Orders" });
        Assert.Equal("staging", result.Aliases["source"].Schema);
        Assert.Contains(new ColumnRef("source", "Total"), result.ColumnRefs);
        Assert.Contains(new ColumnRef("target", "OrderId"), result.ColumnRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "target" or "source");
    }

    [Fact(Skip = "Known gap: MERGE target aliases are not bound by TSqlLineageVisitor.")]
    public void Analyze_Merge_BindsTargetAliasForColumnResolution()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            MERGE dbo.Orders AS target
            USING staging.Orders AS source ON target.OrderId = source.OrderId
            WHEN MATCHED THEN UPDATE SET target.Total = source.Total;
            """);

        Assert.Equal(new ObjectRef("dbo", "Orders"), result.Aliases["target"]);
    }

    [Fact]
    public void Analyze_RecursiveCte_KeepsAnchorAndRecursiveSourcesWithoutLocalName()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            WITH Hierarchy AS (
                SELECT e.Id, e.ManagerId FROM hr.Employees e WHERE e.ManagerId IS NULL
                UNION ALL
                SELECT e.Id, e.ManagerId FROM hr.Employees e
                JOIN Hierarchy h ON e.ManagerId = h.Id
            )
            SELECT h.Id, d.Name FROM Hierarchy h
            JOIN hr.Departments d ON d.ManagerId = h.Id OPTION (MAXRECURSION 100);
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "hr", Name: "Employees" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "hr", Name: "Departments" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "Hierarchy" or "h");
        Assert.Contains(new ColumnRef("e", "ManagerId"), result.ColumnRefs);
    }

    [Fact]
    public void Analyze_CteSharingQualifiedTableName_DoesNotHidePersistentTable()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            WITH Orders AS (SELECT o.Id FROM dbo.Orders o)
            SELECT * FROM Orders;
            """);

        Assert.Equal(new ObjectRef("dbo", "Orders"), Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_ProcedureCallingProcedure_PreservesDatabaseAndIgnoresOutputVariable()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE PROCEDURE dbo.Dispatch AS
            BEGIN
                DECLARE @status int;
                EXEC @status = OperationsDb.ops.ProcessOrders @batchId = 7;
            END;
            """);

        Assert.Equal(new ObjectRef("ops", "ProcessOrders") { Database = "OperationsDb" }, Assert.Single(result.ObjectRefs));
    }

    [Fact]
    public void Analyze_FunctionReadingView_RecordsViewAndReferencedColumn()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE FUNCTION dbo.OrderTotal(@id int) RETURNS decimal(18, 2) AS
            BEGIN
                DECLARE @total decimal(18, 2);
                SELECT @total = v.Total FROM reporting.OrderSummary v WHERE v.OrderId = @id;
                RETURN @total;
            END;
            """);

        Assert.Equal(new ObjectRef("reporting", "OrderSummary"), Assert.Single(result.ObjectRefs));
        Assert.Contains(new ColumnRef("v", "Total"), result.ColumnRefs);
    }

    [Fact]
    public void Analyze_CrossDatabaseJoin_DoesNotCollapseIdenticallyNamedTables()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT a.Id, b.Id FROM CurrentDb.dbo.Orders a
            JOIN ArchiveDb.dbo.Orders b ON b.Id = a.Id;
            """);

        Assert.Contains(new ObjectRef("dbo", "Orders") { Database = "CurrentDb" }, result.ObjectRefs);
        Assert.Contains(new ObjectRef("dbo", "Orders") { Database = "ArchiveDb" }, result.ObjectRefs);
        Assert.Equal("CurrentDb", result.Aliases["a"].Database);
        Assert.Equal("ArchiveDb", result.Aliases["b"].Database);
    }

    [Fact]
    public void Analyze_OpenQueryJoin_DoesNotLeakLinkedServerIntoLocalReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT r.Id, c.Name
            FROM OPENQUERY(SALES_LINK, 'SELECT o.Id FROM SalesDb.dbo.Orders o') r
            JOIN dbo.Customers c ON c.Id = r.Id;
            """);

        Assert.Contains(new ObjectRef("dbo", "Orders") { Database = "SalesDb", Server = "SALES_LINK", Origin = ReferenceOrigin.Dynamic }, result.ObjectRefs);
        Assert.Contains(new ObjectRef("dbo", "Customers"), result.ObjectRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r is { Name: "Customers", Server: not null });
    }

    [Fact]
    public void Analyze_SeparateOpenQueries_PreserveEachLinkedServer()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT * FROM OPENQUERY(EAST_LINK, 'SELECT * FROM dbo.Orders');
            SELECT * FROM OPENQUERY(WEST_LINK, 'SELECT * FROM dbo.Orders');
            """);

        Assert.Contains(new ObjectRef("dbo", "Orders") { Server = "EAST_LINK", Origin = ReferenceOrigin.Dynamic }, result.ObjectRefs);
        Assert.Contains(new ObjectRef("dbo", "Orders") { Server = "WEST_LINK", Origin = ReferenceOrigin.Dynamic }, result.ObjectRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "Orders" && r.Server is null);
    }

    [Fact]
    public void Analyze_JsonExtraction_TracksPayloadColumnWithoutJsonPathObjects()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT JSON_VALUE(e.Payload, '$.customer.id') AS CustomerId,
                   JSON_QUERY(e.Payload, '$.orders') AS Orders
            FROM dbo.Events e;
            """);

        Assert.Equal(new ObjectRef("dbo", "Events"), Assert.Single(result.ObjectRefs));
        Assert.Contains(new ColumnRef("e", "Payload"), result.ColumnRefs);
        Assert.DoesNotContain(result.ColumnRefs, c => c.Column is "id" or "orders");
    }

    [Fact]
    public void Analyze_OpenJsonWithSchema_TracksInputAndDoesNotInventJsonTable()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT j.OrderId FROM dbo.Events e
            CROSS APPLY OPENJSON(e.Payload, '$.orders')
                WITH (OrderId int '$.id') j;
            """);

        Assert.Equal(new ObjectRef("dbo", "Events"), Assert.Single(result.ObjectRefs));
        Assert.Contains(new ColumnRef("e", "Payload"), result.ColumnRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "j" or "OPENJSON");
    }
}
