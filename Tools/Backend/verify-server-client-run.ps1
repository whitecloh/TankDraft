param([Parameter(Mandatory=$true)][string]$RunDirectory, [switch]$Tls)
$ErrorActionPreference = 'Stop'
$clientRunPath = [IO.Path]::GetFullPath($RunDirectory)
$clientFrames0 = @(Get-Content -LiteralPath (Join-Path $clientRunPath 'presentation-0.jsonl') | ConvertFrom-Json)
$clientFrames1 = @(Get-Content -LiteralPath (Join-Path $clientRunPath 'presentation-1.jsonl') | ConvertFrom-Json)
if (!$clientFrames0.Count -or !$clientFrames1.Count) { throw 'Both Unity players must have presentation evidence.' }
$clientFinal0 = $clientFrames0[-1]
$clientFinal1 = $clientFrames1[-1]
if ($clientFinal0.Phase -ne 'MatchResult' -or $clientFinal1.Phase -ne 'MatchResult' -or
    $clientFinal0.Wins0 -ne $clientFinal1.Wins0 -or $clientFinal0.Wins1 -ne $clientFinal1.Wins1 -or
    $clientFinal0.Revision -ne $clientFinal1.Revision -or $clientFinal0.EntitiesHash -ne $clientFinal1.EntitiesHash -or
    [Math]::Max($clientFinal0.Wins0,$clientFinal0.Wins1) -ne 4) { throw 'Final authoritative results differ or are incomplete.' }
if ($clientFinal1.Connections -lt 2 -or !(Test-Path -LiteralPath (Join-Path $clientRunPath 'restart-request-0')) -or
    @($clientFrames0 | Where-Object Resync).Count -lt 2) { throw 'Required disconnect and restart/resync evidence missing.' }
$clientStates = @{}
foreach ($clientFrame in $clientFrames0) { if ($clientFrame.Phase -eq 'Battle') { $clientStates["$($clientFrame.Round):$($clientFrame.Tick)"] = $clientFrame.EntitiesHash } }
$clientMatches = 0
foreach ($clientFrame in $clientFrames1) {
    $clientTickKey = "$($clientFrame.Round):$($clientFrame.Tick)"
    if ($clientFrame.Phase -eq 'Battle' -and $clientStates.ContainsKey($clientTickKey)) {
        if ($clientFrame.EntitiesHash -ne $clientStates[$clientTickKey]) { throw "Entity state mismatch at $clientTickKey" }
        $clientMatches++
    }
}
if ($clientMatches -lt 1) { throw 'No common battle ticks were observed.' }
foreach ($clientField in @('Projectiles','Zones','Effects')) {
    if (($clientFrames0 | Measure-Object -Property $clientField -Maximum).Maximum -le 0) { throw "No rendered $clientField observed." }
}
foreach ($clientSide in @(0,1)) {
    $clientIntent = Get-Content -LiteralPath (Join-Path $clientRunPath "intent-$clientSide.json") -Raw | ConvertFrom-Json
    if ($null -ne $clientIntent.Pending) { throw 'A command is still pending at match completion.' }
    if (Select-String -LiteralPath (Join-Path $clientRunPath "unity-$clientSide.log") -Pattern 'Server client:|Exception|Error' -Quiet) { throw "Unity client $clientSide logged an error." }
    if (!(Test-Path -LiteralPath (Join-Path $clientRunPath "result-$clientSide.png"))) { throw 'Final screenshot missing.' }
}
$clientSummary = [ordered]@{Status='PASS';Round=$clientFinal0.Round;Wins0=$clientFinal0.Wins0;Wins1=$clientFinal0.Wins1;Revision=$clientFinal0.Revision;SharedBattleTicks=$clientMatches;Mismatches=0;Client1Connections=$clientFinal1.Connections}
if ($Tls) {
    $clientExits = @(Get-Content -LiteralPath (Join-Path $clientRunPath 'player-exits.json') -Raw | ConvertFrom-Json)
    if ($clientExits.Count -ne 2 -or @($clientExits | Where-Object { $_ -ne 0 }).Count -ne 0) { throw 'Unity player exit codes are missing or unsuccessful.' }
    foreach ($clientFrames in @($clientFrames0,$clientFrames1)) {
        if (($clientFrames | Measure-Object AuthGeneration -Maximum).Maximum -lt 2) { throw 'Access session did not rotate.' }
        if (@($clientFrames | Select-Object -ExpandProperty StreamId -Unique).Count -ne 1) { throw 'Logical command stream changed during rotation.' }
    }
    if (!(Test-Path -LiteralPath (Join-Path $clientRunPath 'disconnect-request-1'))) { throw 'No deliberate offline interval was recorded.' }
    $clientSummary.AuthGenerations0 = ($clientFrames0 | Measure-Object AuthGeneration -Maximum).Maximum
    $clientSummary.AuthGenerations1 = ($clientFrames1 | Measure-Object AuthGeneration -Maximum).Maximum
}
$clientSummary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $clientRunPath 'verification.json') -Encoding utf8
$clientSummary | ConvertTo-Json -Compress
