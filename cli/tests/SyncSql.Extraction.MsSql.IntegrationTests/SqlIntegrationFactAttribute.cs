namespace SyncSql.Extraction.MsSql.IntegrationTests;

/// <summary>Requires explicit opt-in so solution test runs do not need SQL Server or Docker.</summary>
public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public SqlIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SYNCSQL_RUN_SQL_INTEGRATION") != "1")
        {
            Skip = "Run cli/tests/SyncSql.Extraction.MsSql.IntegrationTests/Run-SqlValidation.ps1 to validate against SQL Server in Docker.";
        }
    }
}
