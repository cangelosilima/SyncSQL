using Microsoft.Extensions.Logging.Abstractions;

using SyncSql.Core.Domain;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class HeterogeneousMsSqlTests
{
    private readonly MsSqlLineageAnalyzer _analyzer = new(NullLogger<MsSqlLineageAnalyzer>.Instance);

    [Fact]
    public void Broker_definitions_and_operations_preserve_their_namespaces()
    {
        var result = _analyzer.Analyze("""
            CREATE CONTRACT [same.name] ([same.name] SENT BY INITIATOR);
            CREATE SERVICE [same.name] ON QUEUE audit.Inbox ([same.name]);
            DECLARE @h uniqueidentifier;
            BEGIN DIALOG @h FROM SERVICE [same.name] TO SERVICE N'target' ON CONTRACT [same.name] WITH ENCRYPTION = OFF;
            SEND ON CONVERSATION @h MESSAGE TYPE [same.name] (N'<item/>');
            RECEIVE TOP (1) message_body FROM audit.Inbox;
            """);
        Assert.Contains(result.ObjectRefs, r => r is { Name: "same.name", ObjectType: "MessageTypes", Schema: null });
        Assert.Contains(result.ObjectRefs, r => r is { Name: "same.name", ObjectType: "Contracts", Schema: null });
        Assert.Contains(result.ObjectRefs, r => r is { Name: "same.name", ObjectType: "Services", Schema: null });
        Assert.Contains(result.ObjectRefs, r => r is { Name: "target", ObjectType: "Services" });
        Assert.Contains(result.ObjectRefs, r => r is { Name: "Inbox", ObjectType: "Queues", Schema: "audit" });
    }

    [Fact]
    public void Broker_variables_and_remote_instances_do_not_invent_local_targets()
    {
        var result = _analyzer.Analyze("""
            DECLARE @h uniqueidentifier, @target nvarchar(128), @type nvarchar(128);
            BEGIN DIALOG @h FROM SERVICE [sender] TO SERVICE @target ON CONTRACT [agreement];
            BEGIN DIALOG @h FROM SERVICE [sender] TO SERVICE N'remote', N'11111111-1111-1111-1111-111111111111' ON CONTRACT [agreement];
            SEND ON CONVERSATION @h MESSAGE TYPE @type (N'payload');
            """);
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "remote" or "@target" or "@type" or "payload");
        Assert.Contains(result.ObjectRefs, r => r is { Name: "sender", ObjectType: "Services" });
    }

    [Fact]
    public void Publication_articles_reference_source_objects_not_article_aliases()
    {
        var result = _analyzer.Analyze("""
            EXEC sys.sp_addarticle @publication = N'pub', @article = N'alias', @source_owner = N'sales', @source_object = N'Item''s';
            EXEC app.sp_addarticle @source_owner = N'fake', @source_object = N'fake';
            EXEC sys.sp_addarticle @source_owner = N'sales', @source_object = @unknown;
            """);
        Assert.Contains(result.ObjectRefs, r => r is { Schema: "sales", Name: "Item's" });
        Assert.DoesNotContain(result.ObjectRefs, r => r.Name is "alias" or "fake" or "@unknown" or "pub");
    }

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
