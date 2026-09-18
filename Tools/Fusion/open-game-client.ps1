param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$runtimeDirectory = Join-Path $privateRoot 'fusion-editor-run'
$gatewayPath = Join-Path $runtimeDirectory 'gateway.json'
$statusPath = Join-Path $runtimeDirectory 'status.json'
$resumeDirectory = Join-Path $privateRoot 'fusion-editor-resume'
$executable = Join-Path $projectRoot 'Builds/Fusion/Client/TankDraftFusionClient.exe'
$knownLogPath = 'not created'
$failure = 'The owner QA client launcher did not complete.'

function Test-FreshReadyStatus {
    param([string]$Path, [DateTime]$AfterUtc = [DateTime]::MinValue, [switch]$RequireCloudReady)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $lastWriteUtc = (Get-Item -LiteralPath $Path).LastWriteTimeUtc
        if ($lastWriteUtc -lt $AfterUtc) { return $false }
        $age = ([DateTime]::UtcNow - $lastWriteUtc).TotalSeconds
        if ($age -lt 0 -or $age -ge 5) { return $false }
        $status = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($RequireCloudReady -and $status.CloudReady -ne $true) { return $false }
        return $status.Ready -eq $true
    } catch {
        return $false
    }
}

function Test-SameClientRunning([string]$ExpectedPath) {
    foreach ($candidate in @(Get-Process -Name 'TankDraftFusionClient' -ErrorAction SilentlyContinue)) {
        try { $candidatePath = $candidate.Path } catch { continue }
        if ($candidatePath -and [string]::Equals([IO.Path]::GetFullPath($candidatePath), $ExpectedPath, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

try {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        $failure = 'The Fusion client build is missing. Build the owner QA client first.'
        throw 'controlled'
    }
    $executable = [IO.Path]::GetFullPath($executable)
    if (Test-SameClientRunning $executable) {
        $failure = 'The owner QA client is already running. Close it before starting another client.'
        throw 'controlled'
    }
    if (Test-FreshReadyStatus -Path $statusPath) {
        $failure = 'Editor Play is already active for this Fusion QA client. Stop Play before launching the standalone client.'
        throw 'controlled'
    }

    $prepareScript = Join-Path $PSScriptRoot 'prepare-editor-play.ps1'
    if (-not (Test-Path -LiteralPath $prepareScript -PathType Leaf)) {
        $failure = 'The existing editor QA preparation helper is missing.'
        throw 'controlled'
    }
    $LASTEXITCODE = 0
    $failure = 'QA preparation failed. Start the server at http://127.0.0.1:18878/, wait for Ready, and retry. If it is Ready, check the VPN connection to PlayFab.'
    & $prepareScript *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'controlled'
    }

    try { $gateway = Get-Content -LiteralPath $gatewayPath -Raw | ConvertFrom-Json } catch {
        $failure = 'The prepared QA gateway configuration is unavailable.'
        throw 'controlled'
    }
    $presentationDirectory = [string]$gateway.PresentationDirectory
    if ([string]::IsNullOrWhiteSpace($presentationDirectory) -or -not [IO.Path]::IsPathRooted($presentationDirectory)) {
        $failure = 'The prepared QA gateway has no valid presentation directory.'
        throw 'controlled'
    }
    $presentationDirectory = [IO.Path]::GetFullPath($presentationDirectory)
    $knownLogPath = Join-Path $presentationDirectory 'player.log'

    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.WorkingDirectory = Split-Path -Parent $executable
    $start.UseShellExecute = $false
    $start.Arguments = '-screen-width 540 -screen-height 960 -screen-fullscreen 0 -logFile "{0}"' -f $knownLogPath
    foreach ($name in @($start.EnvironmentVariables.Keys | Where-Object { $_ -like 'TANKDRAFT_*' -or $_ -like 'TD_*' })) {
        $start.EnvironmentVariables.Remove($name)
    }
    $start.EnvironmentVariables['TANKDRAFT_FUSION_RUNTIME_PATH'] = $gatewayPath
    $start.EnvironmentVariables['TD_FUSION_STATE_DIRECTORY'] = $resumeDirectory
    $launchStartedAt = [DateTime]::UtcNow
    $process = [Diagnostics.Process]::Start($start)

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            $failure = 'The owner QA client exited before it became ready.'
            throw 'controlled'
        }
        if (Test-FreshReadyStatus -Path $statusPath -AfterUtc $launchStartedAt -RequireCloudReady) {
            Write-Host ('Owner QA client ready: {0}' -f $executable)
            Write-Host ('Log: {0}' -f $knownLogPath)
            exit 0
        }
        Start-Sleep -Milliseconds 500
    }
    $failure = 'The owner QA client is still running but did not publish readiness within 60 seconds. It was left open for inspection.'
    throw 'controlled'
} catch {
    Write-Host ('Owner QA client launcher failed: {0}' -f $failure)
    Write-Host ('Log: {0}' -f $knownLogPath)
    exit 1
}
