param(
    [ValidateRange(1, 65535)]
    [int]$Port = 15439
)

$ErrorActionPreference = 'Stop'
$containerName = ('syncsql-definition-check-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$previousPassword = $env:SYNCSQL_VALIDATION_PASSWORD
$previousPort = $env:SYNCSQL_VALIDATION_PORT
$previousOptIn = $env:SYNCSQL_RUN_SQL_INTEGRATION
$created = $false
try {
    $env:SYNCSQL_VALIDATION_PASSWORD = 'V!' + [Guid]::NewGuid().ToString('N') + 'a9'
    $env:SYNCSQL_VALIDATION_PORT = "$Port"
    $env:SYNCSQL_RUN_SQL_INTEGRATION = '1'
    docker run --detach --name $containerName --label 'syncsql.validation=sql-definitions' --publish "127.0.0.1:${Port}:1433" --env ACCEPT_EULA=Y --env "MSSQL_SA_PASSWORD=$env:SYNCSQL_VALIDATION_PASSWORD" mcr.microsoft.com/mssql/server:2022-latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated validation container.' }
    $created = $true
    dotnet test "$PSScriptRoot/SyncSql.Extraction.MsSql.IntegrationTests.csproj" --configuration Release --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        docker logs --tail 80 $containerName
        throw 'SQL validation failed.'
    }
} finally {
    try {
        if ($created) {
            docker rm --force $containerName | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove validation container $containerName." }
        }
    } finally {
        $env:SYNCSQL_VALIDATION_PASSWORD = $previousPassword
        $env:SYNCSQL_VALIDATION_PORT = $previousPort
        $env:SYNCSQL_RUN_SQL_INTEGRATION = $previousOptIn
    }
}
