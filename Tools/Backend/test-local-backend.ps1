$ErrorActionPreference = 'Stop'
$backendRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$backendSdkRoot = Join-Path $backendRepoRoot 'Logs/BackendSdk'
$backendSdkExe = Join-Path $backendSdkRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $backendSdkExe)) { throw 'Run Tools/Backend/bootstrap-dotnet.ps1 first.' }
$backendPreviousDotnetRoot = $env:DOTNET_ROOT
$backendPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_ROOT = $backendSdkRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Push-Location -LiteralPath (Join-Path $backendRepoRoot 'Backend')
    try {
        & $backendSdkExe restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Host restore failed.' }
        & $backendSdkExe restore TankDraft.Server.Security.Tests/TankDraft.Server.Security.Tests.csproj --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
        & $backendSdkExe test TankDraft.Server.Security.Tests/TankDraft.Server.Security.Tests.csproj --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Security tests failed.' }
        & $backendSdkExe restore TankDraft.Server.Match.Tests/TankDraft.Server.Match.Tests.csproj --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Match test restore failed.' }
        & $backendSdkExe test TankDraft.Server.Match.Tests/TankDraft.Server.Match.Tests.csproj --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Server match tests failed.' }
        & $backendSdkExe restore TankDraft.Server.Persistence/TankDraft.Server.Persistence.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Pinned persistence dependency restore failed.' }
        & $backendSdkExe restore TankDraft.Server.Persistence.Tests/TankDraft.Server.Persistence.Tests.csproj --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Persistence test restore failed.' }
        & $backendSdkExe test TankDraft.Server.Persistence.Tests/TankDraft.Server.Persistence.Tests.csproj --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Persistence and process crash tests failed.' }
        & $backendSdkExe run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- --verify
        if ($LASTEXITCODE -ne 0) { throw 'Local HTTP/core verification failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:DOTNET_ROOT = $backendPreviousDotnetRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $backendPreviousTelemetry
}
