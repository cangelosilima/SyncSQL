using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Samples.Benchmark.Tests;

public sealed class OracleDynamicColumnTests
{
    [Theory]
    [InlineData("EXECUTE IMMEDIATE 'SELECT X.ORDER_ID ' || 'FROM APP.ORDERS X' INTO N; EXECUTE IMMEDIATE 'SELECT X.CUSTOMER_ID FROM APP.CUSTOMERS X' INTO N;")]
    [InlineData("EXECUTE IMMEDIATE 'UPDATE APP.ORDERS X SET CUSTOMER_ID = :id WHERE X.ORDER_ID = :id'; EXECUTE IMMEDIATE 'DELETE FROM APP.CUSTOMERS X WHERE X.CUSTOMER_ID = :id';")]
    [InlineData("EXECUTE IMMEDIATE 'MERGE INTO APP.ORDERS X USING APP.CUSTOMERS C ON (X.ORDER_ID = C.CUSTOMER_ID) WHEN MATCHED THEN UPDATE SET CUSTOMER_ID = C.CUSTOMER_ID';")]
    public async Task Catalog_PreservesDynamicColumnsWithoutMixingReusedAliases(string statements)
    {
        string root = Directory.CreateTempSubdirectory("syncsql-oracle-dynamic-").FullName;
        try
        {
            foreach (var obj in new[]
            {
                Object("Tables", "ORDERS", "CREATE TABLE APP.ORDERS (ORDER_ID NUMBER, CUSTOMER_ID NUMBER);"),
                Object("Tables", "CUSTOMERS", "CREATE TABLE APP.CUSTOMERS (ORDER_ID NUMBER, CUSTOMER_ID NUMBER);"),
                Object("StoredProcedures", "REPORT", $"""
                    CREATE PROCEDURE APP.REPORT AS
                        N NUMBER;
                    BEGIN
                        {statements}
                    END;
                    """),
            })
            {
                string path = Path.Combine(root, ExtractedObjectFile.RelativePath(obj.Server, obj.Database, obj.Schema, obj.Type, obj.Name));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, ExtractedObjectFile.Write(obj));
            }

            var catalog = await HeterogeneousLineageTests.BuildCatalogAsync(root);
            var orders = Assert.Single(catalog.Edges, e => e.From.EndsWith("/REPORT", StringComparison.Ordinal)
                && e.To.EndsWith("/ORDERS", StringComparison.Ordinal));
            var customers = Assert.Single(catalog.Edges, e => e.From.EndsWith("/REPORT", StringComparison.Ordinal)
                && e.To.EndsWith("/CUSTOMERS", StringComparison.Ordinal));
            Assert.True(orders.Dynamic);
            Assert.True(customers.Dynamic);
            Assert.Equal(["ORDER_ID"], orders.Columns);
            Assert.Equal(["CUSTOMER_ID"], customers.Columns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ExtractedObject Object(string type, string name, string ddl) => new()
    {
        Server = "ORACLE",
        Database = "APPDB",
        Schema = "APP",
        Type = type,
        Name = name,
        Ddl = ddl,
        Engine = DatabaseEngine.Oracle,
        Columns = type == "Tables"
            ? [new ExtractedColumn("ORDER_ID", "NUMBER", null), new ExtractedColumn("CUSTOMER_ID", "NUMBER", null)]
            : [],
    };
}
