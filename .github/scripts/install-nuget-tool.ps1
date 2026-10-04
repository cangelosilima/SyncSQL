[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [string] $ToolPath,
    [ValidateRange(1, 60)][int] $Attempts = 20,
    [ValidateRange(0, 60)][int] $RetrySeconds = 30
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$config = [IO.Path]::GetTempFileName()
try {
    '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>' | Set-Content $config
    $installArgs = @('tool', 'install', 'SyncSql.Cli', '--version', $Version, '--configfile', $config, '--no-cache')
    if ($ToolPath) { $installArgs += @('--tool-path', $ToolPath) } else { $installArgs += '--global' }
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Write-Host "Installing SyncSql.Cli $Version from nuget.org (attempt $attempt/$Attempts)."
        & dotnet @installArgs
        if ($LASTEXITCODE -eq 0) { return }
        if ($attempt -lt $Attempts) {
            Write-Host "Installation has not succeeded; retrying in $RetrySeconds seconds."
            Start-Sleep -Seconds $RetrySeconds
        }
    }
    throw "Installation of SyncSql.Cli $Version from nuget.org failed after $Attempts attempts. Review the dotnet errors above."
} finally {
    Remove-Item -LiteralPath $config
}
