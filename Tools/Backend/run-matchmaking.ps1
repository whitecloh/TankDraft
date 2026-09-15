param(
    [ValidateSet('windows-pair','windows-solo','android-pair','android-solo')][string]$Mode = 'windows-pair',
    [string]$Target = (Join-Path (Join-Path $PSScriptRoot '../..') 'Logs/BackendClient/Build/TankDraftServerClient.exe'),
    [string]$DeviceSerial,
    [switch]$Automated,
    [switch]$RestartDuringSearch
)
$ErrorActionPreference = 'Stop'
$queueRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$queueSdk = Join-Path $queueRoot 'Logs/BackendSdk'
$queueDotnet = Join-Path $queueSdk 'dotnet.exe'
if (!(Test-Path -LiteralPath $queueDotnet)) { throw 'Local .NET SDK is missing under Logs/BackendSdk.' }
$queueOldRoot = $env:DOTNET_ROOT; $queueOldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_ROOT = $queueSdk; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Push-Location -LiteralPath (Join-Path $queueRoot 'Backend')
    try {
        & $queueDotnet restore TankDraft.LocalHost/TankDraft.LocalHost.csproj --configfile NuGet.Config --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Local backend restore failed.' }
        if ($RestartDuringSearch -and ($Mode -ne 'android-solo' -or !$Automated)) { throw '-RestartDuringSearch requires -Mode android-solo -Automated.' }
        $flag = '--serve-queue-' + $Mode + $(if ($Automated) { '-auto' } else { '' })
        if ($RestartDuringSearch) { $flag = '--serve-queue-android-solo-restart-auto' }
        if ($Mode.StartsWith('android-')) {
            if ([string]::IsNullOrWhiteSpace($DeviceSerial)) { throw 'Android matchmaking requires -DeviceSerial <adb serial>.' }
            & $queueDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- $flag $DeviceSerial $Target
        } else {
            & $queueDotnet run --project TankDraft.LocalHost/TankDraft.LocalHost.csproj --no-restore -- $flag $Target
        }
        if ($LASTEXITCODE -ne 0) { throw 'Local matchmaking session failed.' }
    } finally { Pop-Location }
} finally { $env:DOTNET_ROOT = $queueOldRoot; $env:DOTNET_CLI_TELEMETRY_OPTOUT = $queueOldTelemetry }
