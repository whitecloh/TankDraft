param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$qaRun = [IO.Path]::GetFullPath($RunDirectory)
$qaPhone = Join-Path $qaRun 'phone'
$qaFrames0 = @(Get-Content -LiteralPath (Join-Path $qaRun 'presentation-0.jsonl') | ConvertFrom-Json)
$qaFrames1 = @(Get-Content -LiteralPath (Join-Path $qaPhone 'presentation-1.jsonl') | ConvertFrom-Json)
if (!$qaFrames0.Count -or !$qaFrames1.Count) { throw 'Both clients must have presentation evidence.' }
$qaFinal0 = $qaFrames0[-1]
$qaFinal1 = $qaFrames1[-1]
if ($qaFinal0.Phase -ne 'MatchResult' -or $qaFinal1.Phase -ne 'MatchResult' -or
    $qaFinal0.Wins0 -ne $qaFinal1.Wins0 -or $qaFinal0.Wins1 -ne $qaFinal1.Wins1 -or
    $qaFinal0.Revision -ne $qaFinal1.Revision -or $qaFinal0.EntitiesHash -ne $qaFinal1.EntitiesHash -or
    [Math]::Max($qaFinal0.Wins0,$qaFinal0.Wins1) -ne 4) { throw 'Authoritative final results differ or are incomplete.' }
$qaStates = @{}
foreach ($qaFrame in $qaFrames0) { if ($qaFrame.Phase -eq 'Battle') { $qaStates["$($qaFrame.Round):$($qaFrame.Tick)"] = $qaFrame.EntitiesHash } }
$qaShared = 0
foreach ($qaFrame in $qaFrames1) {
    $qaKey = "$($qaFrame.Round):$($qaFrame.Tick)"
    if ($qaFrame.Phase -eq 'Battle' -and $qaStates.ContainsKey($qaKey)) {
        if ($qaFrame.EntitiesHash -ne $qaStates[$qaKey]) { throw "Entity state differs at $qaKey" }
        $qaShared++
    }
}
if (!$qaShared) { throw 'No shared battle ticks observed.' }
foreach ($qaFrames in @($qaFrames0,$qaFrames1)) {
    if (($qaFrames | Measure-Object AuthGeneration -Maximum).Maximum -lt 2) { throw 'Access renewal was not observed.' }
    if (@($qaFrames | Select-Object -ExpandProperty StreamId -Unique).Count -ne 1) { throw 'Logical command stream changed.' }
}
foreach ($qaSide in @(0,1)) {
    $qaDir = if ($qaSide -eq 0) { $qaRun } else { $qaPhone }
    $qaIntent = Get-Content -LiteralPath (Join-Path $qaDir "intent-$qaSide.json") -Raw | ConvertFrom-Json
    if ($null -ne $qaIntent.Pending) { throw 'Unacknowledged command at completion.' }
    if (Test-Path -LiteralPath (Join-Path $qaDir "failure-$qaSide.txt")) { throw 'Client failure marker found.' }
    if (!(Test-Path -LiteralPath (Join-Path $qaDir "result-$qaSide.png"))) { throw 'Result screenshot missing.' }
}
$qaLifecycle = @(Get-Content -LiteralPath (Join-Path $qaRun 'android-lifecycle.jsonl') | ConvertFrom-Json)
foreach ($qaAction in @('phone-home','battle-after-resume','tunnel-removed','phone-force-stop','phone-restart-after-offline','completed')) {
    if (!($qaLifecycle | Where-Object Action -eq $qaAction)) { throw "Missing lifecycle event: $qaAction" }
}
$qaPause = @(Get-Content -LiteralPath (Join-Path $qaPhone 'lifecycle-1.jsonl') | ConvertFrom-Json)
if (!($qaPause | Where-Object Paused -eq $true) -or !($qaPause | Where-Object Paused -eq $false)) { throw 'Android pause/resume callbacks not observed.' }
$qaFirstPause = $qaPause | Where-Object Paused -eq $true | Select-Object -First 1
if (!($qaPause | Where-Object { !$_.Paused -and [DateTimeOffset]$_.Utc -gt [DateTimeOffset]$qaFirstPause.Utc })) { throw 'No foreground callback after background.' }
$qaStop = $qaLifecycle | Where-Object Action -eq 'phone-force-stop' | Select-Object -Last 1
$qaStart = $qaLifecycle | Where-Object Action -eq 'phone-restart-after-offline' | Select-Object -Last 1
if (([DateTimeOffset]$qaStart.Utc - [DateTimeOffset]$qaStop.Utc).TotalSeconds -lt 40) { throw 'Offline interval too short.' }
$qaBeforeOffline = $qaLifecycle | Where-Object Action -eq 'tunnel-removed' | Select-Object -Last 1
if ($null -eq $qaStart.ServerRevision -or $qaStart.ServerRevision -le $qaBeforeOffline.ServerRevision) { throw 'Server progression during absence not proven.' }
$qaLauncher = Get-Content -LiteralPath (Join-Path $qaRun 'launcher-result.json') -Raw | ConvertFrom-Json
if (!$qaLauncher.Completed -or $qaLauncher.WindowsExit -ne 0) { throw 'Launcher or Windows player failed.' }
if (Select-String -LiteralPath (Join-Path $qaRun 'unity-0.log') -Pattern 'Server client:|Exception|Error' -Quiet) { throw 'Windows runtime error.' }
$qaRuntimeLogs = @(Get-ChildItem -LiteralPath $qaRun -Filter 'phone-runtime-*.log')
if ($qaRuntimeLogs.Count -lt 2) { throw 'Android logs from before and after process restart are required.' }
foreach ($qaRuntimeLog in $qaRuntimeLogs) {
    if (Select-String -LiteralPath $qaRuntimeLog.FullName -Pattern ' E Unity\s*:|FATAL EXCEPTION|Server client:|bootstrap unavailable' -Quiet) { throw 'Android Unity runtime error.' }
}
if (@($qaFrames1 | Where-Object Resync).Count -lt 3) { throw 'Startup, foreground and restart resyncs not observed.' }
$qaSummary = [ordered]@{Status='PASS';Round=$qaFinal1.Round;Wins0=$qaFinal1.Wins0;Wins1=$qaFinal1.Wins1;Revision=$qaFinal1.Revision;SharedBattleTicks=$qaShared;Mismatches=0;AndroidAuthGeneration=($qaFrames1 | Measure-Object AuthGeneration -Maximum).Maximum;Transport='ADB reverse; loopback WSS'}
$qaSummary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRun 'android-verification.json') -Encoding utf8
$qaSummary | ConvertTo-Json -Compress
