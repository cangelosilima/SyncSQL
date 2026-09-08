using Microsoft.SqlServer.TransactSql.ScriptDom;
using SyncSql.Extraction.MsSql.DdlAssembly;
using SyncSql.Extraction.MsSql.Sql;
using SyncSql.Lineage.MsSql;

namespace SyncSql.Extraction.MsSql.Tests.DdlAssembly;

public class DefinitionFidelityTests
{
    private static void AssertValid(string sql)
    {
        using StringReader reader = new(sql);
        TSqlParserFactory.GetParser().Parse(reader, out IList<ParseError> errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(error => error.Message)) + "\n" + sql);
    }

    [Fact]
    public void Schema_PreservesAuthorizationAndEscapesIdentifiers()
    {
        string ddl = SchemaDdlBuilder.Build("Sales]West", "Sales]Owners");
        Assert.Equal("CREATE SCHEMA [Sales]]West] AUTHORIZATION [Sales]]Owners];", ddl);
        AssertValid(ddl);
    }

    [Fact]
    public void Module_PreservesCreationSettingsAndDisabledTrigger()
    {
        string ddl = ModuleDdlBuilder.Build(new ModuleObjectRow
        {
            SchemaName = "dbo",
            ObjectName = "OnOrder",
            TypeCode = "TR",
            UsesAnsiNulls = false,
            UsesQuotedIdentifier = true,
            Definition = "CREATE TRIGGER dbo.OnOrder ON dbo.Orders AFTER INSERT AS SELECT 1;",
            IsDisabled = true,
            ParentSchemaName = "dbo",
            ParentObjectName = "Orders",
        });
        Assert.Contains("SET ANSI_NULLS OFF;\nGO\nSET QUOTED_IDENTIFIER ON;\nGO", ddl);
        Assert.Contains("DISABLE TRIGGER [dbo].[OnOrder] ON [dbo].[Orders];", ddl);
        AssertValid(ddl);
    }

    [Fact]
    public void Columns_PreserveComputedDefaultsUserTypesAndTimeScale()
    {
        string computed = ColumnDdlBuilder.Build(new ColumnDefinitionRow
        {
            ColumnName = "Total",
            ComputedDefinition = "([Quantity]*[Price])",
            IsPersisted = true,
            IsNullable = false,
        });
        string alias = ColumnDdlBuilder.Build(new ColumnDefinitionRow
        {
            ColumnName = "Code",
            TypeName = "CodeType",
            TypeSchema = "Sales",
            IsUserDefined = true,
            CollationName = "SQL_Latin1_General_CP1_CI_AS",
            DefaultName = "DF]Code",
            DefaultDefinition = "('X')",
            IsNullable = false,
        });
        string time = ColumnDdlBuilder.Build(new ColumnDefinitionRow { ColumnName = "Created", TypeName = "datetime2", Scale = 3, IsNullable = true });
        Assert.Equal("[Total] AS ([Quantity]*[Price]) PERSISTED NOT NULL", computed);
        Assert.Equal("[Code] [Sales].[CodeType] NOT NULL CONSTRAINT [DF]]Code] DEFAULT ('X')", alias);
        Assert.Equal("[Created] DATETIME2(3) NULL", time);
        AssertValid($"CREATE TABLE dbo.Orders ([Quantity] int, [Price] decimal(10,2), {computed}, {alias}, {time});");
    }

    [Fact]
    public void Types_ExportAliasTableAndClrDefinitions()
    {
        string alias = TypeDdlBuilder.Build(new TypeRow
        {
            SchemaName = "Sales",
            TypeName = "Code",
            BaseTypeName = "nvarchar",
            MaxLength = 24,
            IsNullable = false,
        }, []);
        string table = TypeDdlBuilder.Build(new TypeRow
        {
            SchemaName = "Sales",
            TypeName = "Ids",
            IsTableType = true,
            ConstraintsDdl = "PRIMARY KEY NONCLUSTERED ([Id] ASC)",
        }, [new ColumnDefinitionRow { ColumnName = "Id", TypeName = "int", IsNullable = false },
            new ColumnDefinitionRow { ColumnName = "Tag", TypeName = "nvarchar", MaxLength = 40, IsNullable = true, CollationName = "SQL_Latin1_General_CP1_CI_AS" }]);
        string clr = TypeDdlBuilder.Build(new TypeRow
        {
            SchemaName = "Sales",
            TypeName = "Geo",
            IsAssemblyType = true,
            AssemblyName = "GeoAssembly",
            AssemblyClass = "App.GeoType",
        }, []);
        Assert.Equal("CREATE TYPE [Sales].[Code] FROM NVARCHAR(12) NOT NULL;", alias);
        Assert.Contains("CREATE TYPE [Sales].[Ids] AS TABLE", table);
        Assert.Contains("PRIMARY KEY NONCLUSTERED", table);
        Assert.Equal("CREATE TYPE [Sales].[Geo] EXTERNAL NAME [GeoAssembly].[App.GeoType];", clr);
        AssertValid(alias); AssertValid(table); AssertValid(clr);
    }

    [Fact]
    public void Index_PreservesFilterOptionsAndDisabledState()
    {
        string ddl = IndexDdlBuilder.Build(new IndexRow
        {
            SchemaName = "dbo",
            TableName = "Orders",
            IndexName = "IX]Open",
            TypeDesc = "NONCLUSTERED",
            KeyColumns = "[Code] ASC",
            IncludedColumns = "[Total]",
            FilterDefinition = "[Closed] = 0",
            FillFactor = 80,
            AllowRowLocks = true,
            AllowPageLocks = false,
            IsDisabled = true,
        });
        Assert.Contains("WHERE [Closed] = 0 WITH (FILLFACTOR = 80", ddl);
        Assert.Contains("ALLOW_PAGE_LOCKS = OFF", ddl);
        Assert.Contains("ALTER INDEX [IX]]Open] ON [dbo].[Orders] DISABLE;", ddl);
        AssertValid(ddl);
    }

    [Fact]
    public void LinkedServer_PreservesGlobalSelfMappingAndSpecificLocalLoginAndOptions()
    {
        LinkedServerRow server = new()
        {
            LinkedServerName = "REMOTE",
            Provider = "MSOLEDBSQL",
            DataSource = "remote.example.com",
            Product = "",
            RpcOut = true,
            DataAccess = true,
            QueryTimeout = 45,
            UseRemoteCollation = true,
            CollationName = null,
        };
        string ddl = LinkedServerDdlBuilder.Build(server,
        [
            WithMapping(null, null, true),
            WithMapping("DOMAIN\\o'brien", "svc", false),
        ]);
        Assert.Contains("sp_droplinkedsrvlogin", ddl);
        Assert.Contains("@locallogin = NULL, @useself = N'TRUE';", ddl);
        Assert.Contains("@locallogin = N'DOMAIN\\o''brien', @useself = N'FALSE', @rmtuser = N'svc'", ddl);
        Assert.Contains("@optname = N'query timeout', @optvalue = N'45'", ddl);
        Assert.Contains("@optname = N'rpc out', @optvalue = N'true'", ddl);
        AssertValid(ddl);

        static LinkedServerRow WithMapping(string? local, string? remote, bool self) => new()
        {
            LocalLoginName = local,
            RemoteLoginName = remote,
            UsesSelfCredential = self,
        };
    }
}
