using Microsoft.Extensions.Logging.Abstractions;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class HeterogeneousMsSqlTests
{
    private readonly MsSqlLineageAnalyzer _analyzer = new(NullLogger<MsSqlLineageAnalyzer>.Instance);

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
