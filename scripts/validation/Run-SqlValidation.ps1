$ErrorActionPreference = 'Stop'
$containerName = ('syncsql-definition-check-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$env:SYNCSQL_VALIDATION_PASSWORD = 'V!' + [Guid]::NewGuid().ToString('N') + 'a9'
$env:SYNCSQL_VALIDATION_PORT = '15439'
$created = $false
try {
    docker run --detach --name $containerName --label 'syncsql.validation=sql-definitions' --publish '127.0.0.1:15439:1433' --env ACCEPT_EULA=Y --env "MSSQL_SA_PASSWORD=$env:SYNCSQL_VALIDATION_PASSWORD" mcr.microsoft.com/mssql/server:2022-latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated validation container.' }
    $created = $true
    dotnet run --project "$PSScriptRoot/SqlValidation.csproj" --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'SQL validation failed.' }
} finally {
    if ($created) { docker rm --force $containerName | Out-Null }
    Remove-Item Env:SYNCSQL_VALIDATION_PASSWORD -ErrorAction SilentlyContinue
}
