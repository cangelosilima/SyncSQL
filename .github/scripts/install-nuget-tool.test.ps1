$global:nugetInstallTestState = @{ exitCodes = @(); calls = @() }
$ErrorActionPreference = 'Stop'
function dotnet {
    $configPath = $args[[Array]::IndexOf($args, '--configfile') + 1]
    [xml] $config = Get-Content $configPath
    Assert-True ($config.configuration.packageSources.add.Count -eq 1) 'Only one public source should be configured.'
    Assert-True ($config.configuration.packageSources.add.value -eq 'https://api.nuget.org/v3/index.json') 'Installation must use nuget.org.'
    Assert-True ($null -ne $config.configuration.packageSources.clear) 'Inherited sources must be cleared.'
    $global:nugetInstallTestState.calls += ,@($args)
    $global:LASTEXITCODE = $global:nugetInstallTestState.exitCodes[$global:nugetInstallTestState.calls.Count - 1]
}
function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

# Download readiness must not be treated as install readiness: retry the CLI itself.
$global:nugetInstallTestState.exitCodes = @(1, 1, 0)
& "$PSScriptRoot/install-nuget-tool.ps1" -Version 2026.10.4.8 -Attempts 3 -RetrySeconds 0
Assert-True ($global:nugetInstallTestState.calls.Count -eq 3) 'The failed install was not retried.'
foreach ($call in $global:nugetInstallTestState.calls) {
    Assert-True ($call -contains '--global') 'Workflow installation must use global scope.'
    Assert-True ($call -contains '2026.10.4.8') 'Every attempt must pin the exact version.'
    Assert-True ($call -contains '--no-cache') 'Every attempt must bypass the HTTP cache.'
    $configPath = $call[[Array]::IndexOf($call, '--configfile') + 1]
    Assert-True (-not (Test-Path $configPath)) 'The source config was not cleaned up.'
}

$global:nugetInstallTestState.calls = @()
$global:nugetInstallTestState.exitCodes = @(1, 1)
$failed = $false
try {
    & "$PSScriptRoot/install-nuget-tool.ps1" -Version 2026.10.4.8 -Attempts 2 -RetrySeconds 0
} catch {
    Assert-True ($_.Exception.Message -like '*failed after 2 attempts*') 'Unexpected installation failure.'
    $failed = $true
}
Assert-True $failed 'Exhausted retries must fail the job.'
Assert-True ($global:nugetInstallTestState.calls.Count -eq 2) 'Retry count exceeded its limit.'

$global:nugetInstallTestState.calls = @()
$global:nugetInstallTestState.exitCodes = @(0)
& "$PSScriptRoot/install-nuget-tool.ps1" -Version 2026.10.4.8 -Attempts 3 -RetrySeconds 0 -ToolPath fixture-tools
Assert-True ($global:nugetInstallTestState.calls.Count -eq 1) 'Successful installation should not be retried.'
Assert-True ($global:nugetInstallTestState.calls[0] -contains '--tool-path') 'Isolated verification must use the requested tool path.'
Assert-True ($global:nugetInstallTestState.calls[0] -notcontains '--global') 'Isolated verification must not install globally.'
Write-Host 'NuGet install retry tests passed.'


Remove-Variable nugetInstallTestState -Scope Global
