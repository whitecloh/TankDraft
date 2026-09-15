$ErrorActionPreference = 'Stop'
$tlsRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tlsSdkRoot = Join-Path $tlsRepoRoot 'Logs/BackendSdk'
$tlsPreviousRoot = $env:DOTNET_ROOT
try {
    $env:DOTNET_ROOT = $tlsSdkRoot
    Push-Location -LiteralPath (Join-Path $tlsRepoRoot 'Backend')
    try {
        & (Join-Path $tlsSdkRoot 'dotnet.exe') restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'TLS host restore failed.' }
        & (Join-Path $tlsSdkRoot 'dotnet.exe') run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- --verify-tls-session
        if ($LASTEXITCODE -ne 0) { throw 'TLS/session verification failed.' }
    }
    finally { Pop-Location }
}
finally { $env:DOTNET_ROOT = $tlsPreviousRoot }
