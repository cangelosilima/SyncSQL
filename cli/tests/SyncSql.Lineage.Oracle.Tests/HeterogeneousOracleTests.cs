using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Lineage.Oracle.Tests;

public sealed class HeterogeneousOracleTests
{
    private readonly OracleLineageAnalyzer _analyzer = new(NullLogger<OracleLineageAnalyzer>.Instance);

    [Theory]
    [InlineData("'SELECT COUNT(*) FROM INVENTORY.ITEMS@REMOTE'")]
    [InlineData("q'[SELECT COUNT(*) FROM INVENTORY.ITEMS@REMOTE]'")]
    public void Literal_dynamic_sql_retains_link_and_origin(string literal)
    {
        string ddl = $"CREATE PROCEDURE PROCUREMENT.P AS N NUMBER; BEGIN EXECUTE IMMEDIATE {literal} INTO N; END;";
        Assert.Contains(_analyzer.Analyze(ddl).ObjectRefs,
            r => r is { Schema: "INVENTORY", Name: "ITEMS", Server: "REMOTE", Origin: ReferenceOrigin.Dynamic });
        Assert.DoesNotContain(_analyzer.Analyze(ddl, new LineageAnalysisOptions { DynamicSql = false }).ObjectRefs,
            r => r.Server == "REMOTE");
    }

    [Theory]
    [InlineData("DBMS_OUTPUT.PUT_LINE('SELECT * FROM INVENTORY.ITEMS@REMOTE');")]
    [InlineData("EXECUTE IMMEDIATE 'SELECT * FROM INVENTORY.' || P_TABLE;")]
    public void Messages_and_unknown_concatenations_do_not_create_lineage(string statement)
    {
        Assert.DoesNotContain(_analyzer.Analyze($"BEGIN {statement} END;").ObjectRefs,
            r => r.Server == "REMOTE" || r.Schema == "INVENTORY");
    }

    [Theory]
    [InlineData("BEGIN COMPLIANCE.COMPLIANCE_API.RUN; END;")]
    [InlineData("BEGIN N := COMPLIANCE.COMPLIANCE_API.F01(); END;")]
    public void Package_member_call_keeps_the_owner(string ddl)
    {
        Assert.Contains(_analyzer.Analyze(ddl).ObjectRefs, r => r is { Schema: "COMPLIANCE", Name: "COMPLIANCE_API" });
        Assert.DoesNotContain(_analyzer.Analyze(ddl).ObjectRefs, r => r.Schema == "COMPLIANCE_API");
    }
}
