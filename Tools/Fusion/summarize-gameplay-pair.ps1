param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$sides=@()
$metrics=@()
function Percentile($values, [double]$fraction) {
    $sorted=@($values | Sort-Object)
    if (!$sorted.Count) { return $null }
    return [Math]::Round($sorted[[Math]::Min($sorted.Count-1,[Math]::Ceiling($sorted.Count*$fraction)-1)],2)
}
$clientCount=if(Test-Path -LiteralPath (Join-Path $RunDirectory 'client-1')){2}else{1}
for($index=0;$index -lt $clientCount;$index++) {
    $path=Join-Path $RunDirectory "client-$index"
    $files=@(Get-ChildItem -LiteralPath $path -Recurse -Filter 'presentation-*.jsonl')
    if (!$files.Count) { throw 'Presentation evidence missing.' }
    $frames=@(foreach($file in $files){Get-Content -LiteralPath $file.FullName | ConvertFrom-Json})
    $battle=@($frames | Where-Object {$_.Phase -eq 'Battle'})
    $hashes=@{}
    foreach($frame in $battle) { $hashes["$($frame.MatchId):$($frame.Round):$($frame.Tick)"]=$frame.EntitiesHash }
    $sides+=,$hashes
    $finals=@($frames | Group-Object MatchId | ForEach-Object {$_.Group | Select-Object -Last 1})
    $held=($finals.HeldFrames | Measure-Object -Sum).Sum; $interpolated=($finals.InterpolatedFrames | Measure-Object -Sum).Sum
    $metrics+=[pscustomobject]@{Client=$index;BattleSamples=$battle.Count;
        NativePresentation=(@($frames | Where-Object {$_.NativePresentation}).Count -gt 0);
        NativeSamples=($finals.NativeSamples | Measure-Object -Sum).Sum;
        NativeMeanGapMs=($finals.NativeMeanGapMs | Measure-Object -Average).Average;
        NativeMaxGapMs=($finals.NativeMaxGapMs | Measure-Object -Maximum).Maximum;
        NativeSourcePollMaxMs=($frames.NativeSourcePollMaxMs | Measure-Object -Maximum).Maximum;
        NativeSourceGapMaxMs=($frames.NativeSourceGapMaxMs | Measure-Object -Maximum).Maximum;
        NativeDecodeMaxMs=($frames.NativeDecodeMaxMs | Measure-Object -Maximum).Maximum;
        NativeStaleStates=($frames.NativeStaleStates | Measure-Object -Maximum).Maximum;
        PollMedianMs=(Percentile @($battle.PollMs) .5);PollP95Ms=(Percentile @($battle.PollMs) .95);PollMaxMs=(Percentile @($battle.PollMs) 1);
        DecodedBytesMedian=(Percentile @($battle.PayloadBytes) .5);
        ConnectionsMax=($frames.Connections | Measure-Object -Maximum).Maximum;
        AuthGenerationMax=($frames.AuthGeneration | Measure-Object -Maximum).Maximum;
        DisconnectedSamples=@($frames | Where-Object {!$_.Connected}).Count;
        HeldRenderFrames=$held;InterpolatedRenderFrames=$interpolated;
        HeldFraction=if($held+$interpolated -gt 0){[Math]::Round($held/($held+$interpolated),4)}else{$null};
        MaxHoldSeconds=($finals.MaxHoldSeconds | Measure-Object -Maximum).Maximum;
        MaxRenderFrameSeconds=($finals.MaxRenderFrameSeconds | Measure-Object -Maximum).Maximum}
}
$shared=0;$mismatches=0
foreach($key in $sides[0].Keys) {
    if($clientCount -eq 2 -and $sides[1].ContainsKey($key)) { $shared++;if($sides[0][$key] -ne $sides[1][$key]) { $mismatches++ } }
}
$summary=[pscustomobject]@{Clients=$metrics;SharedBattleTicks=$shared;EntityHashMismatches=$mismatches;
    Scope='Sampled authoritative entity hashes only. NativeMeanGapMs measures distinct domain ticks reaching SDK render buffers. Native PollMs is local mailbox wait, NOT Photon RTT; legacy PollMs includes gateway/authority. Independent device/fault/visual acceptance is separate.'}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDirectory 'timing-summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 5
if($mismatches) { throw 'Shared battle ticks differ.' }
