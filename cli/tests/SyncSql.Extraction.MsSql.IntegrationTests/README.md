# SQL Server extraction integration tests

This project validates catalog queries and exported DDL against SQL Server 2022.
The round-trip test creates a synthetic source database, executes every extractor
catalog query, extracts eight definitions, replays their DDL and sections into a
second database, and checks 11 source-versus-restored metadata comparisons.

The project belongs to `cli/SyncSql.slnx`. Ordinary `dotnet test` runs discover the
test but skip it without contacting SQL Server or starting Docker.

Run from the repository root with PowerShell, .NET 10, and Docker running:

```powershell
./cli/tests/SyncSql.Extraction.MsSql.IntegrationTests/Run-SqlValidation.ps1
```

The runner starts a temporary `mcr.microsoft.com/mssql/server:2022-latest`
container on localhost port `15439`, enables the test, and removes the container
afterward, including when validation fails. Use `-Port 15440` if the default port
is occupied. The runner restores the caller's environment variables afterward.

For a manually provisioned disposable SQL Server, set
`SYNCSQL_RUN_SQL_INTEGRATION=1`, `SYNCSQL_VALIDATION_PASSWORD`, and
`SYNCSQL_VALIDATION_PORT`, then run `dotnet test` on this project. The connection
uses `sa` at `127.0.0.1`; the test creates `FixtureSource` and `FixtureRestore`
and expects those databases not to exist. Dispose of that server after the run.
