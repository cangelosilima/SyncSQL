using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class PredictionModeTests
{
    [Theory]
    [InlineData("SELECT o.id FROM app.orders o JOIN app.customers c ON c.id = o.customer_id WHERE EXISTS (SELECT 1 FROM app.items i WHERE i.id = o.id);")]
    [InlineData("CREATE OR REPLACE PACKAGE BODY app.pkg AS PROCEDURE run IS BEGIN INSERT INTO app.target(id) SELECT o.id FROM app.orders o; END; END;")]
    [InlineData("BEGIN EXECUTE IMMEDIATE 'SELECT o.id FROM app.orders o'; OPEN :c FOR 'SELECT c.id FROM app.customers c'; END;")]
    [InlineData("CREATE TABLE app.items (id NUMBER, CONSTRAINT fk FOREIGN KEY (id) REFERENCES app.orders(id));")]
    [InlineData("SELECT o.id FROM app.orders@remote o; SELECT c.id FROM app.customers c;")]
    [InlineData("SELECT o.id FROM app.orders o WHERE ; SELECT c.id FROM app.customers c;")]
    public void Analyze_WarmCachePreservesColdFullContextLineage(string sql)
    {
        var cold = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance);
        var warm = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance);
        warm.Analyze("SELECT o.stale FROM other.previous_object o;");
        LineageAnalysisResult expected = cold.Analyze(sql);
        LineageAnalysisResult actual = warm.Analyze(sql);
        Assert.NotEmpty(expected.ObjectRefs);
        Assert.Equal(expected.ObjectRefs, actual.ObjectRefs);
        Assert.Equal(expected.ColumnRefs, actual.ColumnRefs);
        Assert.Equal(expected.Aliases.OrderBy(pair => pair.Key), actual.Aliases.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Analyze_UsesFullContextAndNormalRecoveryInOnePass()
    {
        int calls = 0;
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, parser =>
        {
            calls++;
            Assert.Equal(PredictionMode.LL, parser.Interpreter.PredictionMode);
            Assert.IsType<DefaultErrorStrategy>(parser.ErrorHandler);
            return parser.sql_script();
        });
        var result = analyzer.Analyze("SELECT o.id FROM app.orders o WHERE ; SELECT c.id FROM app.customers c;");
        Assert.Equal(1, calls);
        Assert.Contains(result.ObjectRefs, reference => reference.Name == "orders");
        Assert.Contains(result.ObjectRefs, reference => reference.Name == "customers");
        Assert.Contains(result.ColumnRefs, reference => reference.Column == "id");
    }
}
