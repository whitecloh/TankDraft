param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$faultRunPath = [IO.Path]::GetFullPath($RunDirectory)
& (Join-Path $PSScriptRoot 'verify-server-client-run.ps1') -RunDirectory $faultRunPath -Tls
$faultBase = Get-Content -LiteralPath (Join-Path $faultRunPath 'verification.json') -Raw | ConvertFrom-Json
$faultStats = Get-Content -LiteralPath (Join-Path $faultRunPath 'network-faults.json') -Raw | ConvertFrom-Json
if ($faultStats.profile -ne 'tls-loopback-v1' -or $faultStats.seed -ne 1741 -or !$faultStats.faultsCompleted) { throw 'Network fault profile did not complete.' }
if ($faultStats.failures -ne 0 -or $faultStats.activeAtStop -ne 0 -or $faultStats.maxActive -gt 8) { throw 'Proxy failure or connection leak.' }
if ($faultStats.resetEvents -ne 1 -or $faultStats.resetConnections -lt 1 -or
    $faultStats.stallEvents -ne 1 -or $faultStats.stalledChunks -lt 1 -or $faultStats.stalledConnections -lt 1) { throw 'Faults were scheduled but did not affect live connections.' }
if ($faultStats.clientToServerBytes -le 0 -or $faultStats.serverToClientBytes -le 0 -or $faultStats.delayedChunks -le 1 -or
    $faultStats.delayMinMilliseconds -lt 40 -or $faultStats.delayMaxMilliseconds -gt 120 -or
    $faultStats.delayMinMilliseconds -ge $faultStats.delayMaxMilliseconds -or $faultStats.elapsedMilliseconds -lt 33000) { throw 'No valid latency/jitter/traffic evidence.' }
foreach ($faultSide in @(0,1)) {
    $faultFrames = @(Get-Content -LiteralPath (Join-Path $faultRunPath "presentation-$faultSide.jsonl") | ConvertFrom-Json)
    $faultLastRevision = -1L
    foreach ($faultFrame in $faultFrames) {
        if ($faultFrame.Revision -lt $faultLastRevision) { throw 'Presentation revision went backwards.' }
        $faultLastRevision = $faultFrame.Revision
    }
    if ($faultSide -eq 1 -and !@($faultFrames | Where-Object { $_.Resync -and $_.Round -gt 1 -and $_.AuthGeneration -ge 2 }).Count) {
        throw 'Offline player did not resync to a later round with renewed access.'
    }
    if (Test-Path -LiteralPath (Join-Path $faultRunPath "failure-$faultSide.txt")) { throw 'Unity client reported a fatal failure.' }
}
$faultSummary = [ordered]@{
    Status = 'PASS'
    Profile = $faultStats.profile
    Round = $faultBase.Round
    Wins0 = $faultBase.Wins0
    Wins1 = $faultBase.Wins1
    SharedBattleTicks = $faultBase.SharedBattleTicks
    Mismatches = $faultBase.Mismatches
    ResetConnections = $faultStats.resetConnections
    StalledConnections = $faultStats.stalledConnections
    DelayMinMs = $faultStats.delayMinMilliseconds
    DelayMaxMs = $faultStats.delayMaxMilliseconds
    ClientToServerBytes = $faultStats.clientToServerBytes
    ServerToClientBytes = $faultStats.serverToClientBytes
    AggregateWireKiBPerSecond = [Math]::Round(($faultStats.clientToServerBytes + $faultStats.serverToClientBytes) / 1024.0 / ($faultStats.elapsedMilliseconds / 1000.0), 2)
}
$faultSummary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $faultRunPath 'network-verification.json') -Encoding utf8
$faultSummary | ConvertTo-Json -Compress
