using Microsoft.Extensions.Logging.Abstractions;

using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class HeterogeneousMsSqlTests
{
    private readonly MsSqlLineageAnalyzer _analyzer = new(NullLogger<MsSqlLineageAnalyzer>.Instance);

    [Theory]
    [InlineData("BEGIN PROCUREMENT.PROCUREMENT_API.REFRESH; END;")]
    [InlineData("BEGIN PROCUREMENT.PROCUREMENT_API.REFRESH(); END;")]
    [InlineData("BEGIN /* refresh totals */ PROCUREMENT.PROCUREMENT_API.REFRESH ; END;")]
    public void Remote_begin_marks_package_calls_as_routines_without_requiring_parentheses(string remoteSql)
    {
        var result = _analyzer.Analyze($"EXEC ('{remoteSql}') AT HELIOS_ORACLE;");

        Assert.Contains(result.ObjectRefs, reference => reference is
        {
            Server: "HELIOS_ORACLE", Database: "PROCUREMENT", Schema: "PROCUREMENT_API",
            Name: "REFRESH", IsRoutine: true, Origin: ReferenceOrigin.Dynamic,
        });
    }

    [Fact]
    public void Remote_begin_does_not_mark_subsequent_table_references_as_routines()
    {
        var result = _analyzer.Analyze("EXEC ('BEGIN PROCUREMENT.PROCUREMENT_API.REFRESH; SELECT * FROM Commerce.sales.Orders; END;') AT HELIOS_ORACLE;");

        Assert.Contains(result.ObjectRefs, reference => reference is
        {
            Server: "HELIOS_ORACLE", Database: "Commerce", Schema: "sales", Name: "Orders", IsRoutine: false,
        });
    }

    [Fact]
    public void Trigger_pseudo_tables_refer_to_the_trigger_target()
    {
        var result = _analyzer.Analyze("CREATE TRIGGER audit.WriteLog ON sales.Orders AFTER UPDATE AS INSERT INTO audit.Log SELECT i.Id FROM inserted i JOIN deleted d ON i.Id = d.Id;");
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "sales", Name: "Orders" });
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "audit", Name: "Log" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "inserted" or "deleted");
        Assert.Equal("Orders", result.Aliases["i"].Name);
    }

    [Fact]
    public void Table_named_inserted_outside_a_trigger_remains_a_table()
    {
        Assert.Contains(_analyzer.Analyze("SELECT * FROM inserted;").ObjectRefs, r => r.Name == "inserted");
    }
}
