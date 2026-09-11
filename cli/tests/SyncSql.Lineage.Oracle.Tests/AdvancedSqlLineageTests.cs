using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Lineage.Oracle;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class AdvancedSqlLineageTests
{
    private readonly OracleLineageAnalyzer _analyzer = new(NullLogger<OracleLineageAnalyzer>.Instance);

    [Fact]
    public void Analyze_DatabaseLinkWithDomain_PreservesCompleteLinkAndLocalAlias()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT r.order_id, l.customer_id
            FROM sales.orders@sales.world r
            JOIN app.customers l ON l.customer_id = r.customer_id;
            """);

        Assert.Contains(new ObjectRef("sales", "orders") { Server = "sales.world" }, result.ObjectRefs);
        Assert.Equal("sales.world", result.Aliases["r"].Server);
        Assert.Null(result.Aliases["l"].Server);
        Assert.Contains(new ColumnRef("r", "order_id"), result.ColumnRefs);
    }

    [Fact]
    public void Analyze_TwoDatabaseLinks_DoNotCollapseSameNamedRemoteTables()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT e.order_id FROM app.orders@east_link e
            JOIN app.orders@west_link w ON w.order_id = e.order_id;
            """);

        Assert.Contains(new ObjectRef("app", "orders") { Server = "east_link" }, result.ObjectRefs);
        Assert.Contains(new ObjectRef("app", "orders") { Server = "west_link" }, result.ObjectRefs);
        Assert.Equal("east_link", result.Aliases["e"].Server);
        Assert.Equal("west_link", result.Aliases["w"].Server);
    }

    [Fact]
    public void Analyze_SynonymConsumer_PreservesNameAndColumnForCatalogResolution()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT s.order_id FROM reporting.current_orders s;
            """);

        Assert.Contains(new ObjectRef("reporting", "current_orders"), result.ObjectRefs);
        Assert.Equal(new ObjectRef("reporting", "current_orders"), result.Aliases["s"]);
        Assert.Contains(new ColumnRef("s", "order_id"), result.ColumnRefs);
    }

    [Fact(Skip = "Known gap: PlSqlLineageVisitor does not visit CREATE SYNONYM targets.")]
    public void Analyze_SynonymDefinition_ReferencesRemoteTarget()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE SYNONYM reporting.current_orders FOR app.orders@sales_link;
            """);

        Assert.Contains(new ObjectRef("app", "orders") { Server = "sales_link" }, result.ObjectRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "current_orders");
    }

    [Fact(Skip = "Known gap: Oracle recursive WITH names are not filtered from ObjectRefs.")]
    public void Analyze_RecursiveCte_KeepsBaseTableWithoutLocalSelfReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            WITH hierarchy (id, manager_id) AS (
                SELECT e.id, e.manager_id FROM hr.employees e WHERE e.manager_id IS NULL
                UNION ALL
                SELECT e.id, e.manager_id FROM hr.employees e
                JOIN hierarchy h ON e.manager_id = h.id
            )
            SELECT h.id FROM hierarchy h;
            """);

        Assert.Contains(new ObjectRef("hr", "employees"), result.ObjectRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r is { Schema: null, Name: "hierarchy" });
    }

    [Fact]
    public void Analyze_MaterializedView_TracksAggregateSourceAndGroupingColumn()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE MATERIALIZED VIEW reporting.order_totals
            BUILD IMMEDIATE REFRESH COMPLETE ON DEMAND AS
            SELECT o.customer_id, SUM(o.total) AS total
            FROM app.orders o GROUP BY o.customer_id;
            """);

        Assert.Contains(new ObjectRef("app", "orders"), result.ObjectRefs);
        Assert.Equal("orders", result.Aliases["o"].Name);
        Assert.Contains(new ColumnRef("o", "total"), result.ColumnRefs);
        Assert.Contains(new ColumnRef("o", "customer_id"), result.ColumnRefs);
    }

    [Fact]
    public void Analyze_CtasWithRemoteJoin_PreservesBothSourcesAndLink()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE TABLE reporting.order_snapshot AS
            SELECT o.order_id, c.customer_name
            FROM app.orders o
            JOIN crm.customers@crm_link c ON c.customer_id = o.customer_id;
            """);

        Assert.Contains(new ObjectRef("app", "orders"), result.ObjectRefs);
        Assert.Contains(new ObjectRef("crm", "customers") { Server = "crm_link" }, result.ObjectRefs);
        Assert.Contains(new ColumnRef("c", "customer_name"), result.ColumnRefs);
        Assert.Null(result.Aliases["o"].Server);
    }

    [Fact]
    public void Analyze_Merge_TracksSourceTargetAndColumnEvidence()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            MERGE INTO app.orders t USING staging.orders s
            ON (t.order_id = s.order_id)
            WHEN MATCHED THEN UPDATE SET t.total = s.total
            WHEN NOT MATCHED THEN INSERT (order_id, total) VALUES (s.order_id, s.total);
            """);

        Assert.Contains(new ObjectRef("app", "orders"), result.ObjectRefs);
        Assert.Contains(new ObjectRef("staging", "orders"), result.ObjectRefs);
        Assert.Contains(new ColumnRef("t", "order_id"), result.ColumnRefs);
        Assert.Contains(new ColumnRef("s", "total"), result.ColumnRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "t" or "s");
    }

    [Fact]
    public void Analyze_ProcedureCallingRemoteProcedure_PreservesLink()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE PROCEDURE app.dispatch AS
            BEGIN
                ops.process_orders@ops_link(7);
            END;
            """);

        Assert.Contains(new ObjectRef("ops", "process_orders") { Server = "ops_link", IsRoutine = true }, result.ObjectRefs);
    }

    [Fact]
    public void Analyze_FunctionReadingView_TracksViewAndSelectedColumn()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE FUNCTION app.order_total(p_id NUMBER) RETURN NUMBER AS
                v_total NUMBER;
            BEGIN
                SELECT v.total INTO v_total FROM reporting.order_summary v
                WHERE v.order_id = p_id;
                RETURN v_total;
            END;
            """);

        Assert.Contains(new ObjectRef("reporting", "order_summary"), result.ObjectRefs);
        Assert.Contains(new ColumnRef("v", "total"), result.ColumnRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "v_total");
    }

    [Fact]
    public void Analyze_JsonValue_TracksPayloadWithoutInterpretingPathAsObject()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            SELECT JSON_VALUE(e.payload, '$.customer.id') AS customer_id
            FROM app.events e;
            """);

        Assert.Contains(new ObjectRef("app", "events"), result.ObjectRefs);
        Assert.Contains(new ColumnRef("e", "payload"), result.ColumnRefs);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Schema == "customer" || r.Name == "id");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analyze_ExecuteImmediate_RespectsDynamicSqlOption(bool dynamicSql)
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE PROCEDURE app.refresh_report(p_id NUMBER) AS
            BEGIN
                INSERT INTO app.audit_log (id) VALUES (p_id);
                EXECUTE IMMEDIATE 'DELETE FROM archive.orders WHERE order_id = :id' USING p_id;
            END;
            """, new LineageAnalysisOptions { DynamicSql = dynamicSql });

        Assert.Contains(new ObjectRef("app", "audit_log"), result.ObjectRefs);
        Assert.Equal(dynamicSql, result.ObjectRefs.Any(r =>
            r is { Schema: "archive", Name: "orders", Origin: ReferenceOrigin.Dynamic }));
    }
}
