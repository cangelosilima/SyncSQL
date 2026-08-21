using SyncSql.Extraction.MsSql.DdlAssembly;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public class LinkedServerDdlBuilderTests
{
    [Fact]
    public void Build_NoLogins_OnlyEmitsSpAddLinkedServer()
    {
        string ddl = LinkedServerDdlBuilder.Build("ORAPROD01", "Oracle", "OraOLEDB.Oracle", "orasrc", "provstr", "cat", []);

        Assert.Contains("EXEC sp_addlinkedserver", ddl);
        Assert.Contains("@server = N'ORAPROD01'", ddl);
        Assert.DoesNotContain("sp_addlinkedsrvlogin", ddl);
    }

    [Fact]
    public void Build_WithLogin_MasksPasswordAndKeepsUseSelfFlag()
    {
        string ddl = LinkedServerDdlBuilder.Build(
            "ORAPROD01", "Oracle", "OraOLEDB.Oracle", "orasrc", "provstr", "cat",
            [("app_svc", false)]);

        Assert.Contains("password not extracted", ddl);
        Assert.Contains("@rmtuser = N'app_svc'", ddl);
        Assert.Contains("@rmtpassword = N'########'", ddl);
        Assert.Contains("@useself = N'FALSE'", ddl);
    }

    [Fact]
    public void Build_LoginWithNoRemoteName_IsSkipped()
    {
        string ddl = LinkedServerDdlBuilder.Build("S", "P", "Pr", "D", "PS", "C", [(null, true)]);

        Assert.DoesNotContain("sp_addlinkedsrvlogin", ddl);
    }
}
