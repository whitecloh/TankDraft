$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$base = 'http://127.0.0.1:18878'
$html = (Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 3).Content
$tokenMatch = [regex]::Match($html, 'name="control-token" content="([A-F0-9]{64})"')
if (-not $tokenMatch.Success) { throw 'Server Manager control token is unavailable.' }
$headers = @{ 'X-TankDraft-Control' = $tokenMatch.Groups[1].Value }
$status = Invoke-RestMethod -Uri "$base/api/status" -Headers $headers -TimeoutSec 3
if ($status.state -ne 'Ready') { throw 'Start the Fusion diagnostic server in Server Manager first.' }
$executable = Join-Path $projectRoot 'Builds/Fusion/Server/TankDraftFusionServer.exe'
$runId = [Guid]::NewGuid().ToString('N')
$logs = Join-Path $projectRoot "Logs/FusionServer/pair-$runId"
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$processes = @(); $directories = @()
try {
    for ($side = 0; $side -lt 2; $side++) {
        $auth = Join-Path $privateRoot "fusion-client-$side-auth.json"
        if (-not (Test-Path -LiteralPath $auth)) { throw 'Prepare private QA authentication first.' }
        $directory = Join-Path $privateRoot "fusion-client-$runId-$side"
        New-Item -ItemType Directory -Path $directory | Out-Null
        $directories += $directory
        $runtimePath = Join-Path $directory 'gateway.json'
        @{ Role = 'Client'; AuthPath = $auth; StatusPath = (Join-Path $directory 'status.json'); SessionName = "td-qa-$($status.instanceId)"; LifetimeSeconds = 60 } | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding UTF8
        $env:TANKDRAFT_FUSION_RUNTIME_PATH = $runtimePath
        $arguments = @('-batchmode', '-nographics', '-logFile', ('"{0}"' -f (Join-Path $logs "client-$side.log")))
        $launched = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $null = $launched.Handle
        $processes += $launched
    }
    Remove-Item Env:TANKDRAFT_FUSION_RUNTIME_PATH
    $deadline = [DateTime]::UtcNow.AddSeconds(115)
    while (($processes | Where-Object { -not $_.HasExited }).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    $result = @()
    for ($side = 0; $side -lt 2; $side++) {
        $path = Join-Path $directories[$side] 'status.json'
        if (-not (Test-Path -LiteralPath $path)) { throw 'Client did not publish readiness evidence.' }
        $client = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $result += @{ Side = $side; Replies = $client.Replies; LastReplyAgeSeconds = $client.LastReplyAgeSeconds; ExitCode = $processes[$side].ExitCode }
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'result.json') -Encoding UTF8
    if (@($result | Where-Object { $_.ExitCode -ne 0 -or $_.Replies -lt 20 -or $_.LastReplyAgeSeconds -gt 3 }).Count) { throw 'Client readiness stability failed. See result.json.' }
    Write-Host 'PASS two Fusion clients exchanged authority-readiness messages for 60 seconds. This is not a gameplay match.'
} finally {
    Remove-Item Env:TANKDRAFT_FUSION_RUNTIME_PATH -ErrorAction SilentlyContinue
    foreach ($process in $processes) { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }; $process.Dispose() }
    foreach ($directory in $directories) {
        # Only the known files created by this run; never recursively remove a computed path.
        foreach ($name in @('gateway.json', 'status.json', 'status.json.tmp')) { Remove-Item -LiteralPath (Join-Path $directory $name) -Force -ErrorAction SilentlyContinue }
        if ((Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory }
    }
}
