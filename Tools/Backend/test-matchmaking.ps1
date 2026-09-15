$ErrorActionPreference = 'Stop'
$queueTestRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$queueTestSdkRoot = Join-Path $queueTestRepoRoot 'Logs/BackendSdk'
$queueTestDotnet = Join-Path $queueTestSdkRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $queueTestDotnet)) { throw 'Run Tools/Backend/bootstrap-dotnet.ps1 first.' }
$queueTestPreviousRoot = $env:DOTNET_ROOT
$queueTestPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$queueTestPreviousRollForward = $env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROOT = $queueTestSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
    Push-Location -LiteralPath (Join-Path $queueTestRepoRoot 'Backend')
    try {
        & $queueTestDotnet restore TankDraft.LocalHost.Tests/TankDraft.LocalHost.Tests.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Local matchmaking restore failed.' }
        & $queueTestDotnet test TankDraft.LocalHost.Tests/TankDraft.LocalHost.Tests.csproj --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Local matchmaking tests failed.' }
        & $queueTestDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-build -- --verify-queue-http
        if ($LASTEXITCODE -ne 0) { throw 'Local queue HTTP verification failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $queueTestPreviousRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $queueTestPreviousTelemetry
    $env:DOTNET_ROLL_FORWARD = $queueTestPreviousRollForward
}
