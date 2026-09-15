$ErrorActionPreference = 'Stop'
$clientProtocolRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clientProtocolSdkRoot = Join-Path $clientProtocolRepoRoot 'Logs/BackendSdk'
$clientProtocolDotnet = Join-Path $clientProtocolSdkRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $clientProtocolDotnet)) { throw 'Run Tools/Backend/bootstrap-dotnet.ps1 first.' }
$clientProtocolPreviousRoot = $env:DOTNET_ROOT
$clientProtocolPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$clientProtocolPreviousRollForward = $env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROOT = $clientProtocolSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
    Push-Location -LiteralPath (Join-Path $clientProtocolRepoRoot 'Backend')
    try {
        & $clientProtocolDotnet restore TankDraft.ServerClient.Tests/TankDraft.ServerClient.Tests.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Server client protocol restore failed.' }
        & $clientProtocolDotnet test TankDraft.ServerClient.Tests/TankDraft.ServerClient.Tests.csproj --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Server client protocol tests failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $clientProtocolPreviousRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $clientProtocolPreviousTelemetry
    $env:DOTNET_ROLL_FORWARD = $clientProtocolPreviousRollForward
}
