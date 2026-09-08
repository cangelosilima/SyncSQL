using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;
using SyncSql.Extraction.MsSql;

string password = Environment.GetEnvironmentVariable("SYNCSQL_VALIDATION_PASSWORD")!;
int port = int.Parse(Environment.GetEnvironmentVariable("SYNCSQL_VALIDATION_PORT")!);
string Connection(string database) => new SqlConnectionStringBuilder
{
    DataSource = $"127.0.0.1,{port}", InitialCatalog = database, UserID = "sa", Password = password,
    TrustServerCertificate = true, ConnectTimeout = 3,
}.ConnectionString;
async Task Execute(string database, string sql)
{
    await using SqlConnection connection = new(Connection(database));
    await connection.OpenAsync();
    foreach (string batch in Regex.Split(sql, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(batch)) continue;
        await using SqlCommand command = new(batch, connection) { CommandTimeout = 60 };
        try { await command.ExecuteNonQueryAsync(); }
        catch (SqlException exception) { throw new Exception($"SQL failed in {database}:\n{batch}", exception); }
    }
}
async Task<object?> Scalar(string database, string sql)
{
    await using SqlConnection connection = new(Connection(database));
    await connection.OpenAsync();
    await using SqlCommand command = new(sql, connection);
    return await command.ExecuteScalarAsync();
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
for (int attempt = 0; ; attempt++)
{
    try { await Execute("master", "SELECT 1"); break; }
    catch (Exception) when (attempt < 30) { await Task.Delay(1000); }
}
await Execute("master", "CREATE DATABASE FixtureSource; CREATE DATABASE FixtureRestore;");
await Execute("FixtureSource", """
CREATE ROLE FixtureOwners;
CREATE ROLE FixtureReaders;
GO
CREATE SCHEMA sales AUTHORIZATION FixtureOwners;
GO
GRANT SELECT ON SCHEMA::sales TO FixtureReaders WITH GRANT OPTION;
EXEC sys.sp_addextendedproperty @name=N'CustomFlag', @value=7, @level0type=N'SCHEMA', @level0name=N'sales';
GO
CREATE TYPE sales.Code FROM nvarchar(12) NOT NULL;
GO
CREATE TYPE sales.IdList AS TABLE (Id int NOT NULL, Tag nvarchar(20) NULL DEFAULT N'tag', PRIMARY KEY NONCLUSTERED (Id), CHECK (Id > 0));
GO
CREATE TABLE sales.Parent (Id int NOT NULL CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED);
CREATE TABLE sales.Orders (
    Id int IDENTITY(3,2) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    ParentId int NULL,
    Code sales.Code CONSTRAINT DF_Orders_Code DEFAULT N'X',
    Quantity int NOT NULL,
    Price decimal(10,2) NOT NULL,
    Total AS (Quantity * Price) PERSISTED NOT NULL,
    Created datetime2(3) NULL,
    CONSTRAINT UQ_Orders_Code UNIQUE NONCLUSTERED (Code),
    CONSTRAINT CK_Orders_Quantity CHECK NOT FOR REPLICATION (Quantity > 0),
    CONSTRAINT FK_Orders_Parent FOREIGN KEY (ParentId) REFERENCES sales.Parent(Id) ON DELETE SET NULL ON UPDATE CASCADE NOT FOR REPLICATION
);
ALTER TABLE sales.Orders NOCHECK CONSTRAINT CK_Orders_Quantity;
CREATE NONCLUSTERED INDEX IX_Open ON sales.Orders(ParentId) INCLUDE (Quantity) WHERE ParentId IS NOT NULL WITH (FILLFACTOR=80, ALLOW_PAGE_LOCKS=OFF);
ALTER INDEX IX_Open ON sales.Orders DISABLE;
GRANT SELECT ON OBJECT::sales.Orders TO FixtureReaders WITH GRANT OPTION;
EXEC sys.sp_addextendedproperty @name=N'CustomText', @value=N'O''Brien', @level0type=N'SCHEMA', @level0name=N'sales', @level1type=N'TABLE', @level1name=N'Orders';
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE VIEW sales.OrderSummary AS SELECT Id, Total FROM sales.Orders;
GO
CREATE TRIGGER sales.OnOrder ON sales.Orders AFTER INSERT AS SELECT 1;
GO
DISABLE TRIGGER sales.OnOrder ON sales.Orders;
CREATE SYNONYM sales.OrderAlias FOR sales.Orders;
""");
// Bind every extractor query to real catalog views, not only the syntax parser.
Type queries = typeof(MsSqlObjectExtractor).Assembly.GetType("SyncSql.Extraction.MsSql.Sql.MsSqlQueries")!;
foreach (FieldInfo field in queries.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral && field.FieldType == typeof(string)))
{
    await Execute("FixtureSource", (string)field.GetRawConstantValue()!);
    Console.WriteLine($"Query bound: {field.Name}");
}
ServerConfig server = new()
{
    Name = "ROOT", Type = DatabaseEngine.MsSql, Host = "127.0.0.1", Port = port,
    CredentialsVariablePrefix = "TEST", TrustServerCertificate = true,
    Databases = new NameFilter { Include = ["^FixtureSource$"] }, Schemas = new NameFilter { Include = ["^sales$"] },
    ObjectTypes = ["Schemas", "Types", "Tables", "Views", "Triggers", "Synonyms"],
};
var extractor = new MsSqlObjectExtractor(new ValidationLogger(), TimeProvider.System);
ExtractionOutcome outcome = await extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server),
    new ExtractionOptions { Credentials = new DatabaseCredentials("sa", password), CaptureMetrics = false }, CancellationToken.None);
Check(outcome.Objects.Count == 8, $"Expected 8 objects, got {outcome.Objects.Count}");
await Execute("FixtureRestore", "CREATE ROLE FixtureOwners; CREATE ROLE FixtureReaders;");
int Order(ExtractedObject obj) => obj.Type switch { "Schemas" => 0, "Types" => 1, "Tables" => 2, "Views" => 3, "Triggers" => 4, _ => 5 };
// Create all tables before replaying FK sections; preserve the original batch separators.
foreach (ExtractedObject obj in outcome.Objects.OrderBy(Order))
{
    await Execute("FixtureRestore", obj.Ddl);
    Console.WriteLine($"Created from export: {obj.Type}/{obj.Name}");
}
foreach (ExtractedObject obj in outcome.Objects.OrderBy(Order))
{
    foreach (ExtractedSection section in obj.Sections) await Execute("FixtureRestore", section.Content);
}
string[] comparisons =
[
    "SELECT USER_NAME(principal_id) FROM sys.schemas WHERE name = 'sales'",
    "SELECT CONVERT(nvarchar(100),value) + ':' + CONVERT(nvarchar(100),SQL_VARIANT_PROPERTY(value,'BaseType')) FROM sys.extended_properties WHERE class=3 AND name='CustomFlag'",
    "SELECT state_desc FROM sys.database_permissions WHERE class=3 AND major_id=SCHEMA_ID('sales') AND grantee_principal_id=USER_ID('FixtureReaders')",
    "SELECT state_desc FROM sys.database_permissions WHERE class=1 AND major_id=OBJECT_ID('sales.Orders') AND grantee_principal_id=USER_ID('FixtureReaders')",
    "SELECT definition FROM sys.computed_columns WHERE object_id=OBJECT_ID('sales.Orders') AND name='Total'",
    "SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID('sales.Orders') AND name='CustomText'",
    "SELECT CONCAT(delete_referential_action, ':', update_referential_action, ':',is_not_for_replication) FROM sys.foreign_keys WHERE name='FK_Orders_Parent'",
    "SELECT CONCAT(is_disabled, ':',is_not_trusted, ':',is_not_for_replication) FROM sys.check_constraints WHERE name='CK_Orders_Quantity'",
    "SELECT CONCAT(filter_definition, ':', fill_factor, ':',allow_page_locks, ':',is_disabled) FROM sys.indexes WHERE object_id=OBJECT_ID('sales.Orders') AND name='IX_Open'",
    "SELECT is_disabled FROM sys.triggers WHERE object_id=OBJECT_ID('sales.OnOrder')",
    "SELECT COUNT(*) FROM sys.types WHERE is_user_defined=1 AND schema_id=SCHEMA_ID('sales')",
];
foreach (string comparison in comparisons)
{
    object? source = await Scalar("FixtureSource", comparison), restored = await Scalar("FixtureRestore", comparison);
    Check(Equals(source, restored), $"Restore mismatch: {comparison}: {source} != {restored}");
}
Console.WriteLine($"SQL integration passed: {outcome.Objects.Count} definitions and {comparisons.Length} catalog comparisons.");

sealed class ValidationLogger : ILogger<MsSqlObjectExtractor>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning) throw new Exception(formatter(state, exception), exception);
    }
}
