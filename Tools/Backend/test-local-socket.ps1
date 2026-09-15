$ErrorActionPreference = 'Stop'
$socketRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$socketSdkRoot = Join-Path $socketRepoRoot 'Logs/BackendSdk'
$socketDotnet = Join-Path $socketSdkRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $socketDotnet)) { throw 'Run Tools/Backend/bootstrap-dotnet.ps1 first.' }
$socketPreviousRoot = $env:DOTNET_ROOT
$socketPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_ROOT = $socketSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Push-Location -LiteralPath (Join-Path $socketRepoRoot 'Backend')
    try {
        & $socketDotnet restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Socket host restore failed.' }
        & $socketDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- --verify-socket
        if ($LASTEXITCODE -ne 0) { throw 'Socket verification failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $socketPreviousRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $socketPreviousTelemetry
}
