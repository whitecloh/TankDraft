param([switch]$Automated, [switch]$NetworkFaults)
$ErrorActionPreference = 'Stop'
$clientRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clientSdkRoot = Join-Path $clientRepoRoot 'Logs/BackendSdk'
$clientDotnet = Join-Path $clientSdkRoot 'dotnet.exe'
$clientExe = Join-Path $clientRepoRoot 'Logs/BackendClient/Build/TankDraftServerClient.exe'
if (!(Test-Path -LiteralPath $clientExe)) { throw 'Build ServerMatch through Unity MCP first (Tools/Backend/QueueServerClientBuild.cs).' }
$clientPreviousRoot = $env:DOTNET_ROOT
$clientPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_ROOT = $clientSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Push-Location -LiteralPath (Join-Path $clientRepoRoot 'Backend')
    try {
        & $clientDotnet restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Local backend restore failed.' }
        $clientMode = if ($NetworkFaults) { '--serve-tls-unity-faults' } elseif ($Automated) { '--serve-tls-unity-auto' } else { '--serve-tls-unity' }
        & $clientDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- $clientMode $clientExe
        if ($LASTEXITCODE -ne 0) { throw 'Local client session failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $clientPreviousRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $clientPreviousTelemetry
}
