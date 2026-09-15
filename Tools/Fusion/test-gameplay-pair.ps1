param([int]$MaximumSeconds = 600, [ValidateRange(1,3)][int]$Matches = 1, [switch]$SingleClient, [switch]$CancelFirstSearch, [switch]$CaptureScreenshots, [ValidateRange(0,90)][int]$DisconnectSeconds = 0, [switch]$DisconnectBoth,
    [ValidateRange(0,300)][int]$ColdRestartSeconds = 0, [switch]$RestartBoth, [switch]$RestartPending, [switch]$RestartAfterResult)
$ErrorActionPreference = 'Stop'
if ($MaximumSeconds -lt 60 -or $MaximumSeconds -gt 900) { throw 'Expected 60..900 seconds.' }
if ($ColdRestartSeconds -gt 0 -and ($Matches -ne 1 -or $DisconnectSeconds -gt 0)) { throw 'Cold restart requires one match and no runner fault.' }
if ($RestartAfterResult -and ($SingleClient -or $RestartBoth -or $ColdRestartSeconds -le 0)) { throw 'Result recovery needs one running opponent and one cold client.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$base = 'http://127.0.0.1:18878'
$html = (Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 3).Content
$control = [regex]::Match($html,'name="control-token" content="([A-F0-9]{64})"').Groups[1].Value
$headers = @{'X-TankDraft-Control'=$control}
$status = Invoke-RestMethod "$base/api/status" -Headers $headers -TimeoutSec 3
if ($status.state -ne 'Ready') { throw 'Start the newly built QA server first.' }
$options = Get-Content -LiteralPath (Join-Path $privateRoot 'fusion-server-manager.json') -Raw | ConvertFrom-Json
if (!$options.AllowPlaintextQa) { throw 'Explicit plaintext QA mode is required.' }
$executable = Join-Path $projectRoot 'Builds/Fusion/Client/TankDraftFusionClient.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Gameplay client build missing.' }
$runId = [Guid]::NewGuid().ToString('N')
$logs = Join-Path $projectRoot "Logs/FusionServer/game-$runId"
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$processes = @(); $directories = @()
$starts=@(); $resumeAt=$null; $restarted=$false; $coldEvidence=@()
$clientCount=if($SingleClient){1}else{2}
$faultInjected=$false
try {
    for ($clientIndex=0; $clientIndex -lt $clientCount; $clientIndex++) {
        $auth = Join-Path $privateRoot "fusion-client-$clientIndex-auth.json"
        if (!(Test-Path -LiteralPath $auth)) { throw 'Prepare private QA authentication first.' }
        $directory = Join-Path $privateRoot "fusion-game-$runId-$clientIndex"
        New-Item -ItemType Directory -Path $directory | Out-Null; $directories += $directory
        $presentation = Join-Path $logs "client-$clientIndex"
        New-Item -ItemType Directory -Path $presentation | Out-Null
        $runtimePath = Join-Path $directory 'gateway.json'
        @{ Role='Client'; AuthPath=$auth; StatusPath=(Join-Path $directory 'status.json'); SessionName="td-qa-$($status.instanceId)";
            LifetimeSeconds=$MaximumSeconds; AllowPlaintextQa=$true; PresentationDirectory=$presentation } |
            ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding UTF8
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName=$executable; $start.UseShellExecute=$false; $start.WorkingDirectory=Split-Path -Parent $executable
        $start.Arguments='-screen-width 540 -screen-height 960 -screen-fullscreen 0 -logFile "'+(Join-Path $presentation 'player.log')+'"'
        foreach ($key in @($start.EnvironmentVariables.Keys)) { if ($key -like 'TANKDRAFT_*' -or $key -like 'TD_*') { $start.EnvironmentVariables.Remove($key) } }
        $start.EnvironmentVariables['TANKDRAFT_FUSION_RUNTIME_PATH']=$runtimePath
        $start.EnvironmentVariables['TD_LOCAL_AUTO']='1'; $start.EnvironmentVariables['TD_QUEUE_MODE']='1'
        $start.EnvironmentVariables['TD_FUSION_AUTO_QUEUE']='1'; $start.EnvironmentVariables['TD_QUEUE_AUTO_REMAINING']=[string]$Matches
        $start.EnvironmentVariables['TD_FUSION_STATE_DIRECTORY']=Join-Path $directory 'resume'
        if($ColdRestartSeconds -gt 0 -and $RestartPending -and ($clientIndex -eq 0 -or $RestartBoth)){$start.EnvironmentVariables['TD_FUSION_QA_COLD_PENDING']='1'}
        if($CancelFirstSearch){$start.EnvironmentVariables['TD_FUSION_QA_CANCEL']='1'}
        if($CaptureScreenshots){$start.EnvironmentVariables['TD_FUSION_CAPTURE_SCREENSHOTS']='1'}
        $process=[Diagnostics.Process]::Start($start); $null=$process.Handle; $processes += $process
        $starts += $start
    }
    Write-Output "Fusion gameplay pair running. Evidence: $logs"
    $deadline=[DateTime]::UtcNow.AddSeconds($MaximumSeconds+20)
    while ((@($processes | Where-Object {-not $_.HasExited}).Count -gt 0 -or $null -ne $resumeAt) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if($ColdRestartSeconds -gt 0 -and !$faultInjected){
            $targets=if($RestartBoth){@(0..($clientCount-1))}else{@(0)}
            $coldReady=@()
            foreach($i in $targets){
                $dir=Join-Path $logs "client-$i"
                if($RestartPending){
                    if(Get-ChildItem -LiteralPath $dir -Recurse -Filter 'cold-restart-ready.json'){$coldReady+=$i}
                }else{
                    $last=Get-ChildItem -LiteralPath $dir -Recurse -Filter 'presentation-*.jsonl'|Select-Object -Last 1
                    if($last){foreach($line in (Get-Content -LiteralPath $last.FullName -Tail 2)){try{$frame=$line|ConvertFrom-Json;if($frame.Phase -eq 'Battle' -and $frame.Connected){$coldReady+=$i;break}}catch{}}}
                }
            }
            if($coldReady.Count -eq $targets.Count){
                foreach($i in $targets){
                    $dir=Join-Path $logs "client-$i"
                    Stop-Process -Id $processes[$i].Id; $processes[$i].WaitForExit()
                    $assignmentFile=Get-ChildItem -LiteralPath $dir -Recurse -Filter 'assignment.json'|Select-Object -Last 1
                    $assignment=Get-Content -LiteralPath $assignmentFile.FullName -Raw|ConvertFrom-Json
                    $journalFile=Get-ChildItem -LiteralPath (Join-Path $directories[$i] 'resume') -Recurse -Filter 'intent.json'|Select-Object -Last 1
                    $journal=Get-Content -LiteralPath $journalFile.FullName -Raw|ConvertFrom-Json
                    $ackFile=Get-ChildItem -LiteralPath $dir -Recurse -Filter 'cold-restart-ready.json'|Select-Object -Last 1
                    $ack=if($ackFile){Get-Content -LiteralPath $ackFile.FullName -Raw|ConvertFrom-Json}else{$null}
                    if($RestartPending -and (!$journal.Pending -or $journal.Pending.OperationId -ne $ack.OperationId)){throw 'Missing persisted unacknowledged server command.'}
                    $presentationFile=Get-ChildItem -LiteralPath $dir -Recurse -Filter 'presentation-*.jsonl'|Select-Object -Last 1
                    $lineCount=if($presentationFile){@(Get-Content -LiteralPath $presentationFile.FullName).Count}else{0}
                    $coldEvidence+=@{Client=$i;Assignment=$assignment;JournalBefore=$journal;ServerAckBeforeKill=$ack;OldPid=$processes[$i].Id;PresentationLinesBefore=$lineCount}
                    Move-Item -LiteralPath (Join-Path $dir 'player.log') -Destination (Join-Path $dir 'player-before-restart.log')
                }
                $resumeAt=[DateTime]::UtcNow.AddSeconds($ColdRestartSeconds); $faultInjected=$true
                $coldEvidence|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $logs 'cold-restart-before.json') -Encoding UTF8
                @{Utc=[DateTime]::UtcNow.ToString('o');Clients=$targets;Seconds=$(if($RestartAfterResult){$null}else{$ColdRestartSeconds});AfterResult=[bool]$RestartAfterResult;Kind='KillProcessThenColdStart';Pending=[bool]$RestartPending}|ConvertTo-Json|Set-Content (Join-Path $logs 'fault.json')
                if($RestartAfterResult){Write-Output 'Killed QA client process. Cold restart after the opponent receives the final result.'}
                else{Write-Output "Killed QA client processes. Cold restart in $ColdRestartSeconds seconds."}
            }
        }
        $resultReady=$false
        if($RestartAfterResult -and $null -ne $resumeAt){
            $last=Get-ChildItem -LiteralPath (Join-Path $logs 'client-1') -Recurse -Filter 'presentation-*.jsonl'|Select-Object -Last 1
            if($last){foreach($line in (Get-Content -LiteralPath $last.FullName -Tail 2)){try{$frame=$line|ConvertFrom-Json;if($frame.Phase -eq 'MatchResult'){$resultReady=$true}}catch{}}}
        }
        if($null -ne $resumeAt -and (($RestartAfterResult -and $resultReady) -or (!$RestartAfterResult -and [DateTime]::UtcNow -ge $resumeAt))){
            foreach($entry in $coldEvidence){
                $i=$entry.Client; $processes[$i].Dispose()
                $starts[$i].EnvironmentVariables.Remove('TD_FUSION_QA_COLD_PENDING')
                $processes[$i]=[Diagnostics.Process]::Start($starts[$i]); $null=$processes[$i].Handle; $entry.NewPid=$processes[$i].Id
            }
            $resumeAt=$null; $restarted=$true; Write-Output 'Fresh client processes started; awaiting authoritative recovery.'
        }
        if($DisconnectSeconds -gt 0 -and !$faultInjected){
            $readyClients=@()
            for($i=0;$i -lt $clientCount;$i++){
                $last=Get-ChildItem -LiteralPath (Join-Path $logs "client-$i") -Recurse -Filter 'presentation-*.jsonl' | Select-Object -Last 1
                if($last){foreach($line in (Get-Content -LiteralPath $last.FullName -Tail 2)){try{$frame=$line|ConvertFrom-Json;if($frame.Phase -eq 'Battle' -and $frame.Connected){$readyClients+=$i;break}}catch{}}}
            }
            if($readyClients.Count -eq $clientCount){
                $targets=if($DisconnectBoth){@(0..($clientCount-1))}else{@(0)}
                foreach($i in $targets){
                    $private=Join-Path $privateRoot "fusion-game-$runId-$i"
                    [IO.File]::WriteAllText((Join-Path $private 'reconnect-delay-seconds'),[string]$DisconnectSeconds)
                    [IO.File]::WriteAllText((Join-Path $private 'disconnect'),'qa')
                }
                @{Utc=[DateTime]::UtcNow.ToString('o');Clients=$targets;Seconds=$DisconnectSeconds;Kind='RealRunnerShutdownThenRecreate'}|ConvertTo-Json|Set-Content (Join-Path $logs 'fault.json')
                $faultInjected=$true
            }
        }
        foreach($playerLog in Get-ChildItem -LiteralPath $logs -Recurse -Filter 'player.log'){
            if((Get-Content -LiteralPath $playerLog.FullName -Tail 12) -match 'FUSION_QA_GAME_FLOW_FAILED|FUSION_QA_GAME_FLOW_TIMEOUT|FUSION_START_FAILED|^Server client:'){
                @{Reason='ClientInitializationOrTransportFailed';Client=$playerLog.Directory.Name} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'failure.json')
                throw "Client failed; inspect private QA evidence in $logs"
            }
        }
    }
    $results=@()
    if($ColdRestartSeconds -gt 0 -and !$restarted){throw 'Cold application restart was not exercised.'}
    if($DisconnectSeconds -gt 0 -and !$faultInjected){throw 'Reconnect fault was not exercised.'}
    for ($clientIndex=0; $clientIndex -lt $clientCount; $clientIndex++) {
        $directory=Join-Path $logs "client-$clientIndex"
        if($ColdRestartSeconds -gt 0 -and ($clientIndex -eq 0 -or $RestartBoth)){
            $entry=$coldEvidence|Where-Object {$_.Client -eq $clientIndex}
            $journalFile=Get-ChildItem -LiteralPath (Join-Path $directories[$clientIndex] 'resume') -Recurse -Filter 'intent.json'|Select-Object -Last 1
            $entry.JournalAfter=Get-Content -LiteralPath $journalFile.FullName -Raw|ConvertFrom-Json
            $presentationFile=Get-ChildItem -LiteralPath $directory -Recurse -Filter 'presentation-*.jsonl'|Select-Object -Last 1
            $entry.FirstFrameAfter=Get-Content -LiteralPath $presentationFile.FullName|Select-Object -Skip $entry.PresentationLinesBefore -First 1|ConvertFrom-Json
            if(!$entry.FirstFrameAfter -or $entry.FirstFrameAfter.MatchId -ne $entry.Assignment.MatchId -or ($RestartAfterResult -and $entry.FirstFrameAfter.Phase -ne 'MatchResult')){throw 'Cold start did not project the current server state.'}
            $assignments=@(Get-Content -LiteralPath (Join-Path $directory 'assignments.jsonl')|ConvertFrom-Json)
            if($assignments.Count -ne 2 -or @($assignments|Where-Object {$_.MatchId -ne $entry.Assignment.MatchId -or $_.Side -ne $entry.Assignment.Side}).Count){throw 'Cold restart did not restore the same assignment.'}
            if($RestartPending){
                $receipts=@(Get-ChildItem -LiteralPath $directory -Recurse -Filter 'command-receipts.jsonl'|ForEach-Object {Get-Content -LiteralPath $_.FullName}|ConvertFrom-Json)
                $reconciled=@($receipts|Where-Object {$_.OperationId -eq $entry.JournalBefore.Pending.OperationId})
                if($reconciled.Count -ne 1 -or $reconciled[0].Sequence -ne $entry.JournalBefore.Pending.Sequence -or $reconciled[0].Code -ne $entry.ServerAckBeforeKill.Reply.Code -or $entry.JournalAfter.Pending){throw 'Exact pending command was not reconciled once.'}
                $entry.ReconciledReceipt=$reconciled[0]
            }
        }
        if($DisconnectSeconds -gt 0 -and ($clientIndex -eq 0 -or $DisconnectBoth)){
            if(!(Select-String -LiteralPath (Join-Path $directory 'player.log') -SimpleMatch 'FUSION_RECONNECT_READY serial=2' -Quiet)){throw 'SDK runner was not recreated successfully.'}
        }
        $matchDirectories=@(Get-ChildItem -LiteralPath $directory -Directory -Filter 'match-*' | Sort-Object CreationTimeUtc)
        if($matchDirectories.Count -ne $Matches){throw "Expected $Matches matches for client $clientIndex; see $directory"}
        foreach($matchDirectory in $matchDirectories){
        $file=Get-ChildItem -LiteralPath $matchDirectory.FullName -Filter 'presentation-*.jsonl' | Select-Object -First 1
        $final=$null
        if($file) { foreach($line in (Get-Content -LiteralPath $file.FullName -Tail 12)) { $frame=$line | ConvertFrom-Json; if($frame.Phase -eq 'MatchResult') { $final=$frame } } }
        $assignmentPath=Join-Path $matchDirectory.FullName 'assignment.json'
        $assignment=if(Test-Path -LiteralPath $assignmentPath) { Get-Content -LiteralPath $assignmentPath -Raw | ConvertFrom-Json } else { $null }
        $code=if($processes[$clientIndex].HasExited){$processes[$clientIndex].ExitCode}else{$null}
        $results+=@{ Client=$clientIndex; Directory=$matchDirectory.FullName; ExitCode=$code; Assignment=$assignment; Final=$final }
        }
        if($CancelFirstSearch){
            $events=@(Get-Content -LiteralPath (Join-Path $directory 'queue.jsonl') | ConvertFrom-Json)
            if(!@($events | Where-Object {$_.Operation -eq 'Cancel' -and $_.State -eq 'Idle'}).Count){throw 'No confirmed cancellation evidence.'}
        }
    }
    $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $logs 'result.json') -Encoding UTF8
    if($ColdRestartSeconds -gt 0){$coldEvidence|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $logs 'cold-restart.json') -Encoding UTF8}
    if (@($results | Where-Object {$_.ExitCode -ne 0 -or !$_.Final}).Count) { throw 'Gameplay pair did not complete. See result.json and player logs.' }
    $expectedOpponent=if($SingleClient){'Bot'}else{'Human'}
    if (@($results | Where-Object {$_.Assignment.OpponentKind -ne $expectedOpponent -or $_.Assignment.MatchId -ne $_.Final.MatchId}).Count) { throw 'Unexpected assignment or opponent.' }
    $groups=@($results | Group-Object {$_.Final.MatchId})
    if($groups.Count -ne $Matches){throw 'Unexpected distinct match count.'}
    foreach($group in $groups){
        $entries=@($group.Group)
        if($entries.Count -ne $clientCount){throw 'Clients did not share the same match.'}
        if(!$SingleClient -and ($entries[0].Assignment.Side -eq $entries[1].Assignment.Side -or $entries[0].Final.Wins0 -ne $entries[1].Final.Wins0 -or $entries[0].Final.Wins1 -ne $entries[1].Final.Wins1)){throw 'Sides or final results differ.'}
        Write-Output "PASS $expectedOpponent match $($group.Name), score $($entries[0].Final.Wins0):$($entries[0].Final.Wins1)."
    }
} finally {
    foreach ($process in $processes) { if (!$process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
    foreach ($directory in $directories) {
        foreach ($name in @('gateway.json','status.json','status.json.tmp')) { Remove-Item -LiteralPath (Join-Path $directory $name) -Force -ErrorAction SilentlyContinue }
        if ((Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory }
    }
}
