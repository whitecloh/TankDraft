param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [ValidateSet('Human', 'Bot')][string]$ExpectedOpponent = 'Human',
    [ValidateRange(1, 20)][int]$ExpectedMatches = 2,
    [switch]$AllowReconnect
)

$ErrorActionPreference = 'Stop'
$matchmakingRun = [IO.Path]::GetFullPath($RunDirectory)
if (!(Test-Path -LiteralPath $matchmakingRun -PathType Container)) { throw "Run directory does not exist: $matchmakingRun" }

function Read-JsonLines([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing evidence: $Path" }
    $items = @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim().Length -gt 0 } | ConvertFrom-Json)
    if (!$items.Count) { throw "Empty evidence: $Path" }
    return $items
}

function Find-PlayerDirectory([int]$Index) {
    $candidates = if ($Index -eq 0) {
        @((Join-Path $matchmakingRun 'player-0'))
    } else {
        @((Join-Path $matchmakingRun 'player-1'), (Join-Path $matchmakingRun 'phone'), (Join-Path $matchmakingRun 'android/phone'))
    }
    $found = @($candidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'matches') -PathType Container })
    if ($found.Count -gt 1) { throw "More than one match evidence directory found for player $Index." }
    if ($found.Count -eq 1) { return $found[0] }
    return $null
}

function Percentile95([object[]]$Values) {
    $numbers = @($Values | ForEach-Object { [double]$_ } | Sort-Object)
    if (!$numbers.Count) { return $null }
    return $numbers[[Math]::Min($numbers.Count - 1, [Math]::Ceiling($numbers.Count * .95) - 1)]
}

function Get-MatchRecord([string]$Player, [string]$Directory) {
    $assignmentPath = Join-Path $Directory 'assignment.json'
    if (!(Test-Path -LiteralPath $assignmentPath -PathType Leaf)) { throw "Missing assignment: $assignmentPath" }
    $assignment = Get-Content -LiteralPath $assignmentPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($assignment.MatchId) -or $assignment.Side -notin @(0, 1) -or $assignment.OpponentKind -notin @('Human', 'Bot')) {
        throw "Invalid assignment: $assignmentPath"
    }
    if ($assignment.OpponentKind -ne $ExpectedOpponent) { throw "Unexpected opponent kind for $($assignment.MatchId): $($assignment.OpponentKind)" }
    $frames = Read-JsonLines (Join-Path $Directory ("presentation-{0}.jsonl" -f $assignment.Side))
    $final = $frames[-1]
    if ($final.Phase -ne 'MatchResult' -or [Math]::Max([int]$final.Wins0, [int]$final.Wins1) -ne 4) {
        throw "Match $($assignment.MatchId) did not finish with an authoritative 4-win result."
    }
    if ($final.Pending -eq $true) { throw "Presentation still has a pending command at $($assignment.MatchId)." }
    $intentPath = Join-Path $Directory ("intent-{0}.json" -f $assignment.Side)
    if (!(Test-Path -LiteralPath $intentPath -PathType Leaf)) { throw "Missing intent journal: $intentPath" }
    if ($null -ne (Get-Content -LiteralPath $intentPath -Raw | ConvertFrom-Json).Pending) { throw "Intent journal still has a pending command at $($assignment.MatchId)." }
    $maxConnections = ($frames | Measure-Object -Property Connections -Maximum).Maximum
    if (!$AllowReconnect -and $maxConnections -ne 1) { throw "Expected one seamless connection for $($assignment.MatchId), got $maxConnections." }
    $maxGeneration = ($frames | Measure-Object -Property AuthGeneration -Maximum).Maximum
    if ($maxGeneration -lt 2) { throw "Access renewal was not observed for $($assignment.MatchId)." }
    if (!$AllowReconnect -and @($frames | Where-Object { $_.Phase -eq 'Battle' -and $_.Connected -ne $true }).Count) {
        throw "Disconnected Battle frame found for $($assignment.MatchId)."
    }
    $streams = @($frames | Select-Object -ExpandProperty StreamId -Unique)
    if ($streams.Count -ne 1 -or [string]::IsNullOrWhiteSpace($streams[0])) { throw "Logical stream changed for $($assignment.MatchId)." }
    return [pscustomobject]@{
        Player = $Player; Directory = $Directory; MatchId = [string]$assignment.MatchId; Side = [int]$assignment.Side; OpponentKind = [string]$assignment.OpponentKind
        Frames = $frames; Final = $final; MaxConnections = [int]$maxConnections; MaxGeneration = [int]$maxGeneration; StreamId = [string]$streams[0]
        CatchupFrames = @($frames | Where-Object { $_.CatchingUp -eq $true }).Count; FrameSeconds = @($frames | Where-Object { $null -ne $_.FrameSeconds } | Select-Object -ExpandProperty FrameSeconds)
    }
}

$players = @()
for ($index = 0; $index -le 1; $index++) {
    $directory = Find-PlayerDirectory $index
    if ($null -ne $directory) { $players += [pscustomobject]@{ Name = "player-$index"; Directory = $directory } }
}
if (!$players.Count) { throw 'No player match evidence directories found.' }
if ($ExpectedOpponent -eq 'Human' -and $players.Count -ne 2) { throw 'Human verification requires both player-0 and player-1/phone evidence.' }

$records = @()
foreach ($player in $players) {
    $matchDirectories = @(Get-ChildItem -LiteralPath (Join-Path $player.Directory 'matches') -Directory)
    if ($matchDirectories.Count -ne $ExpectedMatches) { throw "$($player.Name) has $($matchDirectories.Count) completed match directories; expected $ExpectedMatches." }
    foreach ($matchDirectory in $matchDirectories) { $records += Get-MatchRecord $player.Name $matchDirectory.FullName }
}

$sharedTicks = 0
if ($ExpectedOpponent -eq 'Human') {
    $groups = @($records | Group-Object MatchId)
    if ($groups.Count -ne $ExpectedMatches) { throw "Expected $ExpectedMatches unique human match IDs, got $($groups.Count)." }
    foreach ($group in $groups) {
        if ($group.Count -ne 2 -or @($group.Group | Select-Object -ExpandProperty Side -Unique).Count -ne 2) { throw "Human match $($group.Name) does not have one record per side." }
        $first = $group.Group[0]; $second = $group.Group[1]
        if ($first.Final.Wins0 -ne $second.Final.Wins0 -or $first.Final.Wins1 -ne $second.Final.Wins1 -or $first.Final.Revision -ne $second.Final.Revision) { throw "Human final state differs for $($group.Name)." }
        $firstBattle = @{}
        foreach ($frame in $first.Frames) { if ($frame.Phase -eq 'Battle') { $firstBattle["$($frame.Round):$($frame.Tick)"] = $frame } }
        $localShared = 0
        foreach ($frame in $second.Frames) {
            $key = "$($frame.Round):$($frame.Tick)"
            if ($frame.Phase -eq 'Battle' -and $firstBattle.ContainsKey($key)) {
                $other = $firstBattle[$key]
                if ($frame.EntitiesHash -ne $other.EntitiesHash -or $frame.Wins0 -ne $other.Wins0 -or $frame.Wins1 -ne $other.Wins1) { throw "Human Battle state differs for $($group.Name) at $key." }
                $localShared++
            }
        }
        if (!$localShared) { throw "No shared Battle ticks for human match $($group.Name)." }
        $sharedTicks += $localShared
    }
} else {
    if (@($records | Select-Object -ExpandProperty MatchId -Unique).Count -ne $records.Count) { throw 'Bot match IDs must be unique per player assignment.' }
}

$allFrameSeconds = @($records | ForEach-Object { $_.FrameSeconds })
$summary = [ordered]@{
    Status = 'PASS'; ExpectedOpponent = $ExpectedOpponent; ExpectedMatchesPerPlayer = $ExpectedMatches; Players = $players.Count
    MatchRecords = $records.Count; UniqueMatches = @($records | Select-Object -ExpandProperty MatchId -Unique).Count; SharedBattleTicks = $sharedTicks
    CatchupFrames = @($records | Measure-Object -Property CatchupFrames -Sum).Sum; FrameSecondsP95 = Percentile95 $allFrameSeconds
    Connections = @($records | ForEach-Object { $_.MaxConnections }); AuthGenerations = @($records | ForEach-Object { $_.MaxGeneration }); AllowReconnect = [bool]$AllowReconnect
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $matchmakingRun 'matchmaking-verification.json') -Encoding utf8
$summary | ConvertTo-Json -Compress
