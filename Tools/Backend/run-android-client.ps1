param(
    [Parameter(Mandatory = $true)][string]$Serial,
    [switch]$Automated
)
$ErrorActionPreference = 'Stop'
$androidRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$androidSdkRoot = Join-Path $androidRepoRoot 'Logs/BackendSdk'
$androidDotnet = Join-Path $androidSdkRoot 'dotnet.exe'
$androidClientExe = Join-Path $androidRepoRoot 'Logs/BackendClient/Build/TankDraftServerClient.exe'
if (!(Test-Path -LiteralPath $androidClientExe)) { throw 'Build ServerMatch through Unity MCP first (Tools/Backend/QueueServerClientBuild.cs).' }
$androidOldDotnetRoot = $env:DOTNET_ROOT
$androidOldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_ROOT = $androidSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Push-Location -LiteralPath (Join-Path $androidRepoRoot 'Backend')
    try {
        & $androidDotnet restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Local backend restore failed.' }
        $androidMode = if ($Automated) { '--serve-android-auto' } else { '--serve-android' }
        & $androidDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- $androidMode $Serial
        if ($LASTEXITCODE -ne 0) { throw 'Android local client session failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $androidOldDotnetRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $androidOldTelemetry
}
