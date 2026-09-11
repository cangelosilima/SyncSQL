using Antlr4.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class VisitorBoundaryTests
{
    private static PlSqlParser Parser(string sql)
    {
        var lexer = new PlSqlLexer(new AntlrInputStream(sql));
        lexer.RemoveErrorListeners();
        var parser = new PlSqlParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        return parser;
    }

    [Fact]
    public void Visitor_HandlesIncompleteTableRoutineAndExpressionNodes()
    {
        var visitor = new PlSqlLineageVisitor();
        visitor.VisitTableview_name(new PlSqlParser.Tableview_nameContext(null, 0));
        visitor.VisitRoutine_name(new PlSqlParser.Routine_nameContext(null, 0));
        visitor.VisitGeneral_element(new PlSqlParser.General_elementContext(null, 0));
        visitor.VisitTable_ref_aux(new PlSqlParser.Table_ref_auxContext(null, 0));
        var table = new PlSqlParser.Table_ref_auxContext(null, 0);
        table.AddChild(new PlSqlParser.Table_ref_aux_internal_oneContext(new PlSqlParser.Table_ref_aux_internalContext(null, 0)));
        visitor.VisitTable_ref_aux(table);
        Assert.Empty(visitor.ObjectRefs);
        Assert.Empty(visitor.ColumnRefs);
        var listener = new CollectingErrorListener();
        listener.SyntaxError(TextWriter.Null, null!, 0, 1, 2, "bad character", null!);
        Assert.Equal("line 1:2 bad character", Assert.Single(listener.Errors));
    }

    [Theory]
    [InlineData("SELECT t.id FROM t;")]
    [InlineData("SELECT t.id FROM (SELECT id FROM t) t;")]
    [InlineData("BEGIN app.pkg.run(); END;")]
    [InlineData("BEGIN EXECUTE IMMEDIATE q'[SELECT * FROM app.orders]'; END;")]
    [InlineData("BEGIN EXECUTE IMMEDIATE Q'{SELECT * FROM app.orders}'; END;")]
    public void Analyzer_RecognizesUnqualifiedNestedAndAlternativeQuotedSql(string sql)
    {
        var result = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance).Analyze(sql);
        Assert.NotEmpty(result.ObjectRefs);
    }

    [Theory]
    [InlineData("BEGIN EXECUTE IMMEDIATE x; END;")]
    [InlineData("BEGIN EXECUTE IMMEDIATE 12345; END;")]
    [InlineData("BEGIN EXECUTE IMMEDIATE 'SELECT * FROM app.t' || suffix; END;")]
    public void Analyzer_DoesNotInventDynamicTargetsForUnknownExpressions(string sql)
    {
        var result = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance).Analyze(sql);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Origin == ReferenceOrigin.Dynamic);
    }

    [Fact]
    public void DynamicSql_EnforcesLengthCountAndDepthBudgets()
    {
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance);
        string statement = "EXECUTE IMMEDIATE 'SELECT * FROM app.orders;';";
        var many = analyzer.Analyze("BEGIN " + string.Concat(Enumerable.Repeat(statement, 70)) + " END;");
        Assert.NotEmpty(many.ObjectRefs);
        Assert.True(many.ObjectRefs.Count < 140);
        string large = "SELECT * FROM app.orders " + new string(' ', 65536);
        Assert.Empty(analyzer.Analyze("BEGIN EXECUTE IMMEDIATE '" + large + "'; END;").ObjectRefs);
        string nested = "SELECT * FROM app.orders;";
        for (int i = 0; i < 6; i++) { nested = "BEGIN EXECUTE IMMEDIATE '" + nested.Replace("'", "''", StringComparison.Ordinal) + "'; END;"; }
        Assert.Empty(analyzer.Analyze(nested).ObjectRefs);
    }

    [Fact]
    public void GeneralElements_HandleFlatAndParenthesizedGrammarAlternatives()
    {
        var visitor = new PlSqlLineageVisitor();
        var flat = new PlSqlParser.General_elementContext(null, 0);
        foreach (string part in new[] { "app", "pkg", "run()" }) { flat.AddChild(Parser(part).general_element_part()); }
        visitor.VisitGeneral_element(flat);
        Assert.Contains(visitor.ObjectRefs, r => r.Schema == "app" && r.Name == "pkg");
        var pair = new PlSqlParser.General_elementContext(null, 0);
        pair.AddChild(Parser("app").general_element_part());
        pair.AddChild(Parser("run()").general_element_part());
        visitor.VisitGeneral_element(pair);
        Assert.Contains(visitor.ObjectRefs, r => r.Schema == "app" && r.Name == "run");
        visitor.Visit(Parser("(app).run()").general_element());
        visitor.Visit(Parser("run()").general_element());
        Assert.NotEmpty(visitor.ObjectRefs);
    }

    [Fact]
    public void Analyzer_IncompleteExecuteImmediateDoesNotEscapeAsAnException()
    {
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance);
        Assert.Empty(analyzer.Analyze("BEGIN EXECUTE IMMEDIATE ; END;").ObjectRefs);
    }

    [Fact]
    public void Analyzer_ContainsParserFailuresButPropagatesMemoryExhaustion()
    {
        var analyzer = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, _ => throw new InvalidOperationException("parser failed"));
        Assert.Same(LineageAnalysisResult.Empty, analyzer.Analyze("SELECT * FROM app.orders;"));
        var exhausted = new OracleLineageAnalyzer(NullLogger<OracleLineageAnalyzer>.Instance, _ => throw new OutOfMemoryException("simulated"));
        Assert.Throws<OutOfMemoryException>(() => exhausted.Analyze("SELECT * FROM app.orders;"));
    }
}
