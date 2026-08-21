using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Lineage.Oracle;

namespace SyncSql.Lineage.Oracle.Tests;

/// <summary>
/// Includes the exact edge cases already proven against the old comment/string-scrubbing approach
/// (comments, string literals, q'...' quoting, quoted identifiers) - now expected to hold for a
/// fundamentally different reason: a real lexer simply never tokenizes string/comment content as an
/// identifier in the first place, rather than a scrubbing pass trying to blank it out first.
/// </summary>
public class OracleLineageAnalyzerTests
{
    private readonly OracleLineageAnalyzer _analyzer = new(NullLogger<OracleLineageAnalyzer>.Instance);

    [Fact]
    public void Analyze_ViewWithJoinAndAliases_ResolvesObjectRefsAndAliases()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE VIEW "APP"."ORDER_SUMMARY" ("ORDER_ID", "CUSTOMER_NAME") AS
            SELECT o.order_id, c.customer_name
            FROM app.orders o
            JOIN app.customers c ON o.customer_id = c.customer_id;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "customers" });
        Assert.Equal("orders", result.Aliases["o"].Name);
        Assert.Equal("customers", result.Aliases["c"].Name);
    }

    [Fact]
    public void Analyze_TableWithForeignKeyReferences_ResolvesReferenceTarget()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE TABLE "APP"."ORDERS"
               (	"ORDER_ID" NUMBER NOT NULL ENABLE,
                    "CUSTOMER_ID" NUMBER,
                     CONSTRAINT "FK_ORDERS_CUSTOMERS" FOREIGN KEY ("CUSTOMER_ID")
                      REFERENCES "APP"."CUSTOMERS" ("CUSTOMER_ID") ENABLE
               ) ;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "APP", Name: "CUSTOMERS" });
    }

    [Fact]
    public void Analyze_LineCommentMentioningAnObjectName_IsNotTreatedAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE VIEW app.weird AS
            SELECT 1 AS note -- references app.fake_table but is just a comment
            FROM app.orders;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "fake_table");
    }

    [Fact]
    public void Analyze_BlockCommentMentioningAnObjectName_IsNotTreatedAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE VIEW app.weird AS
            SELECT 1 AS note /* references app.fake_table
            across lines */
            FROM app.orders;
            """);

        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "fake_table");
    }

    [Fact]
    public void Analyze_StringLiteralMentioningAnObjectName_IsNotTreatedAsAReference()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE VIEW app.weird AS
            SELECT 'this text mentions app.fake_table but is just a string literal' AS note
            FROM app.orders;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "fake_table");
    }

    [Fact]
    public void Analyze_AlternativeQuotedStringMentioningAnObjectName_IsNotTreatedAsAReference()
    {
        // Oracle's q'[...]' alternative quoting - the construct the old regex-based approach had no
        // way to recognize at all.
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE VIEW app.weird AS
            SELECT q'[SELECT * FROM app.fake_table]' AS note
            FROM app.orders;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "orders" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name == "fake_table");
    }

    [Fact]
    public void Analyze_SchemaQualifiedFunctionCall_ResolvesAsObjectRef()
    {
        LineageAnalysisResult result = _analyzer.Analyze("""
            CREATE OR REPLACE PROCEDURE app.do_stuff AS
            BEGIN
                app.other_proc();
                DBMS_OUTPUT.PUT_LINE(app.my_func(1));
            END;
            """);

        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "other_proc" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "app", Name: "my_func" });
    }

    [Fact]
    public void Analyze_EmptyOrWhitespaceDdl_ReturnsEmptyResult()
    {
        Assert.Empty(_analyzer.Analyze("").ObjectRefs);
        Assert.Empty(_analyzer.Analyze("   ").ObjectRefs);
    }

    [Fact]
    public void Analyze_UnparseableDdl_DegradesRatherThanThrowing()
    {
        // ANTLR (like ScriptDom) does error-recovery parsing - garbage input logs a warning and may
        // still yield some partial/wrong structure rather than nothing at all. The contract this
        // guards is "never throws", not "always empty on bad input".
        LineageAnalysisResult result = _analyzer.Analyze("THIS IS NOT VALID PL/SQL !!! (((");

        Assert.NotNull(result);
    }
}
