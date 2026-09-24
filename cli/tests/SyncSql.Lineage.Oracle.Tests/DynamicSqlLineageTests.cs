using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using Antlr4.Runtime;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class DynamicSqlLineageTests
{
    private readonly OracleLineageAnalyzer _analyzer = new(NullLogger<OracleLineageAnalyzer>.Instance);

    [Theory]
    [InlineData("EXECUTE IMMEDIATE 'SELECT o.order_id FROM app.orders@remote o'")]
    [InlineData("EXECUTE IMMEDIATE ('SELECT o.order_id ' || ('FROM app.' || 'orders@remote o'))")]
    [InlineData("EXECUTE IMMEDIATE q'[SELECT o.order_id ]' || /* separator */ q'{FROM app.orders@remote o}'")]
    [InlineData("EXECUTE IMMEDIATE N'SELECT o.order_id FROM app.orders@remote o'")]
    [InlineData("OPEN result_cursor FOR 'SELECT o.order_id FROM app.orders@remote o'")]
    [InlineData("OPEN result_cursor FOR 'SELECT o.order_id ' || 'FROM app.orders@remote o WHERE o.order_id = :id' USING p_id")]
    [InlineData("EXECUTE IMMEDIATE 'SELECT o.order_id FROM app.orders@remote o WHERE o.status = ''ACTIVE''' ")]
    public void KnownSql_PreservesObjectsAndColumnBindings(string statement)
    {
        var result = _analyzer.Analyze($"BEGIN {statement}; END;");
        Assert.Contains(result.ObjectRefs, r => r is
        { Schema: "app", Name: "orders", Server: "remote", Origin: ReferenceOrigin.Dynamic });
        Assert.Contains(result.ColumnRefs, c => c.Column == "order_id"
            && result.Aliases.TryGetValue(c.AliasOrTable, out var target)
            && target is { Schema: "app", Name: "orders", Server: "remote", Origin: ReferenceOrigin.Dynamic });

        var disabled = _analyzer.Analyze($"BEGIN {statement}; END;", new LineageAnalysisOptions { DynamicSql = false });
        Assert.DoesNotContain(disabled.ObjectRefs, r => r.Origin == ReferenceOrigin.Dynamic);
        Assert.DoesNotContain(disabled.ColumnRefs, c => c.Column == "order_id");
    }

    [Fact]
    public void ReusedAliases_KeepColumnsBoundToTheirOwnStatement()
    {
        var result = _analyzer.Analyze("""
            BEGIN
                EXECUTE IMMEDIATE 'SELECT x.order_id FROM app.orders x';
                SELECT x.audit_id INTO v_id FROM app.audit_log x;
                EXECUTE IMMEDIATE 'SELECT x.customer_id FROM app.customers x';
                EXECUTE IMMEDIATE 'BEGIN EXECUTE IMMEDIATE ''SELECT x.item_id FROM app.items x''; END;';
            END;
            """);

        Assert.Equal("audit_log", result.Aliases["x"].Name);
        foreach (var (column, table) in new[] { ("order_id", "orders"), ("audit_id", "audit_log"),
            ("customer_id", "customers"), ("item_id", "items") })
        {
            ColumnRef reference = Assert.Single(result.ColumnRefs, c => c.Column == column);
            Assert.Equal(table, result.Aliases[reference.AliasOrTable].Name);
        }
    }

    [Theory]
    [InlineData("EXECUTE IMMEDIATE sql_text")]
    [InlineData("EXECUTE IMMEDIATE 'SELECT o.order_id FROM app.orders o' || suffix")]
    [InlineData("OPEN result_cursor FOR 'SELECT o.order_id FROM app.' || table_name || ' o'")]
    [InlineData("EXECUTE IMMEDIATE REPLACE('SELECT o.order_id FROM app.orders o', 'orders', table_name)")]
    [InlineData("DBMS_OUTPUT.PUT_LINE('SELECT o.order_id FROM app.orders o')")]
    public void UnknownExpressionsAndMessages_DoNotInventDependencies(string statement)
    {
        var result = _analyzer.Analyze($"BEGIN {statement}; END;");
        Assert.DoesNotContain(result.ObjectRefs, r => r.Origin == ReferenceOrigin.Dynamic);
        Assert.DoesNotContain(result.ColumnRefs, c => c.Column == "order_id");
    }

    [Fact]
    public void StaticOpenFor_RemainsStaticWhenDynamicSqlIsDisabled()
    {
        var result = _analyzer.Analyze("BEGIN OPEN result_cursor FOR SELECT o.order_id FROM app.orders o; END;",
            new LineageAnalysisOptions { DynamicSql = false });
        Assert.Contains(new ObjectRef("app", "orders"), result.ObjectRefs);
        Assert.Contains(new ColumnRef("o", "order_id"), result.ColumnRefs);
    }

    [Fact]
    public void IncompleteOrRecoveredExpressions_AreNotScanned()
    {
        var visitor = new PlSqlLineageVisitor(_ => throw new InvalidOperationException("Unexpected dynamic scan"));
        visitor.VisitExecute_immediate(new PlSqlParser.Execute_immediateContext(null, 0));
        var statement = new PlSqlParser.Execute_immediateContext(null, 0);
        var expression = new PlSqlParser.ExpressionContext(statement, 0);
        expression.AddErrorNode(new CommonToken(PlSqlLexer.CHAR_STRING, "'SELECT * FROM app.orders'"));
        statement.AddChild(expression);
        visitor.VisitExecute_immediate(statement);
        Assert.Empty(visitor.ObjectRefs);
    }

    [Fact]
    public void DeeplyNestedLiteralExpression_RespectsEvaluationDepthLimit()
    {
        string sql = "BEGIN EXECUTE IMMEDIATE " + new string('(', 20)
            + "'SELECT * FROM app.orders'" + new string(')', 20) + "; END;";
        Assert.Empty(_analyzer.Analyze(sql).ObjectRefs);
    }

    [Fact]
    public void Concatenation_RespectsCombinedSizeLimit()
    {
        string padding = new(' ', 33000);
        var result = _analyzer.Analyze($"BEGIN EXECUTE IMMEDIATE 'SELECT o.order_id FROM app.orders o{padding}' || '{padding}'; END;");
        Assert.Empty(result.ObjectRefs);
        Assert.Empty(result.ColumnRefs);
    }
}
