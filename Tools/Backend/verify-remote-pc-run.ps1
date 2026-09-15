param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = [IO.Path]::GetFullPath($RunDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $repo 'Logs/RemoteClient')) + [IO.Path]::DirectorySeparatorChar
if (!$root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Expected a RemoteClient evidence directory.' }
@{ Status='INCOMPLETE'; Reason='Verification has not completed successfully.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'verification.json')
$run = Get-Content -LiteralPath (Join-Path $root 'run.json') -Raw | ConvertFrom-Json
if (@($run.Clients).Count -ne 2 -or $run.Matches -lt 1 -or $run.Matches -gt 3) { throw 'Expected two bounded QA clients.' }

function Read-Lines([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing evidence: $([IO.Path]::GetFileName($Path))" }
    @(Get-Content -LiteralPath $Path | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
}
function Summary($Frames) {
    $gaps = @()
    for ($i = 1; $i -lt $Frames.Count; $i++) {
        $a = $Frames[$i-1]; $b = $Frames[$i]
        if ($a.Phase -eq 'Battle' -and $b.Phase -eq 'Battle' -and $a.Round -eq $b.Round -and $a.Connections -eq $b.Connections) {
            $gaps += 1000 * ($b.ClientTime - $a.ClientTime)
        }
    }
    $ordered = @($gaps | Sort-Object)
    [ordered]@{ Samples=$ordered.Count; MedianMs=$(if($ordered.Count){$ordered[[int][Math]::Floor(($ordered.Count-1)*.5)]}else{$null});
        P95Ms=$(if($ordered.Count){$ordered[[int][Math]::Ceiling(($ordered.Count-1)*.95)]}else{$null});
        MaxMs=$(if($ordered.Count){$ordered[-1]}else{$null}); Connections=($Frames.Connections | Measure-Object -Maximum).Maximum }
}
$clients = @()
foreach ($client in $run.Clients) {
    $dir = [IO.Path]::GetFullPath($client.RunDirectory)
    if (!$dir.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Client evidence escaped its run.' }
    if (@(Get-ChildItem -LiteralPath $dir -Filter 'failure-*.txt').Count -gt 0 -or (Test-Path -LiteralPath (Join-Path $dir 'queue-error.txt'))) { throw 'Client failure evidence exists.' }
    $assignments = @(Read-Lines (Join-Path $dir 'remote-assignments.jsonl'))
    if ($assignments.Count -ne $run.Matches -or @($assignments | Where-Object { $_.OpponentKind -ne 'Human' -or $_.InstanceId -ne $run.InstanceId }).Count) { throw 'Expected exactly the requested Human assignments on this instance.' }
    if (@($assignments.MatchId | Sort-Object -Unique).Count -ne $run.Matches) { throw 'Repeated assignment is not a new match.' }
    $frames = @()
    foreach ($file in Get-ChildItem -LiteralPath $dir -Filter 'presentation-*.jsonl') { $frames += @(Read-Lines $file.FullName) }
    if (!$frames.Count -or @($frames | Where-Object { $_.DiagnosticsVersion -ne 2 -or !$_.MatchId }).Count) { throw 'Frame-bound diagnostics v2 required.' }
    $faults = @()
    if ($run.DisconnectLastMatch) { $faults = @(Read-Lines (Join-Path $dir 'remote-qa-faults.jsonl')) }
    $clients += [pscustomobject]@{ Assignments=$assignments; Frames=$frames; Faults=$faults }
}
$matches = @()
for ($index = 0; $index -lt $run.Matches; $index++) {
    $id = $clients[0].Assignments[$index].MatchId
    if ($id -ne $clients[1].Assignments[$index].MatchId -or $clients[0].Assignments[$index].Side -eq $clients[1].Assignments[$index].Side) { throw 'Clients did not share opposite sides of the same match.' }
    $a = @($clients[0].Frames | Where-Object MatchId -eq $id | Sort-Object ClientTime)
    $b = @($clients[1].Frames | Where-Object MatchId -eq $id | Sort-Object ClientTime)
    $finalA = @($a | Where-Object Phase -eq 'MatchResult'); $finalB = @($b | Where-Object Phase -eq 'MatchResult')
    if (!$finalA.Count -or !$finalB.Count) { throw 'Both clients must display the final result.' }
    $fa=$finalA[-1]; $fb=$finalB[-1]
    if ($fa.Wins0 -ne $fb.Wins0 -or $fa.Wins1 -ne $fb.Wins1 -or [Math]::Max($fa.Wins0,$fa.Wins1) -ne 4 -or $fa.EntitiesHash -ne $fb.EntitiesHash) { throw 'Final score or state mismatch.' }
    $lookup=@{}; foreach($frame in $a){if($frame.Phase -eq 'Battle'){$lookup["$($frame.Round):$($frame.Tick)"]=$frame.EntitiesHash}}
    $shared=@{}; foreach($frame in $b){$key="$($frame.Round):$($frame.Tick)";if($frame.Phase -eq 'Battle' -and $lookup.ContainsKey($key)){if($lookup[$key] -ne $frame.EntitiesHash){throw 'Shared battle state mismatch.'};$shared[$key]=$true}}
    if (!$shared.Count) { throw 'No shared battle ticks available for comparison.' }
    $outages=@()
    if ($run.DisconnectLastMatch -and $index -eq $run.Matches-1) {
        for($side=0;$side -lt 2;$side++) {
            $faults=@($clients[$side].Faults | Where-Object MatchId -eq $id)
            if($faults.Count -ne 1 -or $faults[0].Seconds -ne 40) { throw 'Expected one forty-second socket outage per client in the last match.' }
            $returned=@($clients[$side].Frames | Where-Object { $_.MatchId -eq $id -and $_.Connections -ge 2 } | Sort-Object ClientTime)
            if(!$returned.Count) { throw 'No successful reconnect after the planned outage.' }
            $seconds=$returned[0].ClientTime-$faults[0].ClientTime
            if($seconds -lt 39 -or $seconds -gt 70) { throw 'Observed outage duration outside test bounds.' }
            $outages += $seconds
        }
    }
    $matches += [ordered]@{MatchId=$id;Wins0=$fa.Wins0;Wins1=$fa.Wins1;SharedBattleTicks=$shared.Count;HashMismatches=0;Client0=(Summary $a);Client1=(Summary $b);OutageSeconds=$outages}
}
$report=[ordered]@{Status='PASS';Scope='Two PC clients; complete Human matches and requested socket fault only';Matches=$matches;DeviceLifecycleAccepted=$false;VmLossRecoveryAccepted=$false}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'verification.json')
$report | ConvertTo-Json -Depth 8
