param(
    [Parameter(Mandatory = $true)][string]$RunDirectory
)

$ErrorActionPreference = 'Stop'
$remoteRun = [IO.Path]::GetFullPath($RunDirectory)
if (!(Test-Path -LiteralPath $remoteRun -PathType Container)) { throw 'Remote client evidence directory does not exist.' }

function Read-JsonObject([string]$Path, [string]$Name) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing $Name evidence." }
    $text = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Empty $Name evidence." }
    try { return $text | ConvertFrom-Json }
    catch { throw "Invalid $Name JSON evidence." }
}

function Read-JsonLines([string]$Path, [string]$Name) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing $Name evidence." }
    $items = @()
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $items += ($line | ConvertFrom-Json) }
        catch { throw "Invalid $Name JSON line evidence." }
    }
    if (!$items.Count) { throw "Empty $Name evidence." }
    return $items
}

function Require-Property([object]$Value, [string]$Name, [string]$Evidence) {
    if ($null -eq $Value -or $Value.PSObject.Properties.Name -notcontains $Name -or $null -eq $Value.$Name) {
        throw "Missing $Name in $Evidence evidence."
    }
    return $Value.$Name
}

function Require-Integer([object]$Value, [string]$Name, [long]$Minimum, [long]$Maximum, [string]$Evidence) {
    $raw = Require-Property $Value $Name $Evidence
    [long]$number = 0
    if (![long]::TryParse([string]$raw, [ref]$number) -or $number -lt $Minimum -or $number -gt $Maximum) {
        throw "Invalid $Name in $Evidence evidence."
    }
    return $number
}

function Require-Text([object]$Value, [string]$Name, [string]$Pattern, [string]$Evidence) {
    $text = [string](Require-Property $Value $Name $Evidence)
    if ($text -notmatch $Pattern) { throw "Invalid $Name in $Evidence evidence." }
    return $text
}

$run = Read-JsonObject (Join-Path $remoteRun 'run.json') 'run'
$endpointText = Require-Text $run 'Endpoint' '^https://' 'run'
try { $endpoint = [uri]$endpointText } catch { throw 'Invalid Endpoint in run evidence.' }
if ($endpoint.Scheme -cne 'https' -or $endpoint.DnsSafeHost -cnotmatch '^[a-f0-9]{12,64}\.pr\.edgegap\.net$' -or
    $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment -or $endpoint.AbsolutePath -cne '/' -or $endpoint.Port -lt 1 -or $endpoint.Port -gt 65535) {
    throw 'Invalid Endpoint in run evidence.'
}
$instanceId = Require-Text $run 'InstanceId' '^[a-f0-9]{32}$' 'run'

$records = @()
foreach ($index in @(0, 1)) {
    $clientDirectory = Join-Path $remoteRun ("client-{0}" -f $index)
    if (!(Test-Path -LiteralPath $clientDirectory -PathType Container)) { throw "Missing client-$index evidence directory." }
    $assignment = Read-JsonObject (Join-Path $clientDirectory 'remote-assignment.json') "client-$index assignment"
    if ((Require-Text $assignment 'State' '^Matched$' "client-$index assignment") -cne 'Matched') { throw "Invalid client-$index assignment state." }
    $matchId = Require-Text $assignment 'MatchId' '^[a-f0-9]{32}-[a-f0-9]{32}$' "client-$index assignment"
    if (!$matchId.StartsWith($instanceId + '-', [StringComparison]::Ordinal)) { throw "client-$index assignment belongs to another instance." }
    $assignmentInstance = Require-Text $assignment 'InstanceId' '^[a-f0-9]{32}$' "client-$index assignment"
    if ($assignmentInstance -cne $instanceId) { throw "client-$index assignment instance differs from run evidence." }
    $side = Require-Integer $assignment 'Side' 0 1 "client-$index assignment"
    if ((Require-Text $assignment 'OpponentKind' '^Human$' "client-$index assignment") -cne 'Human') { throw "client-$index assignment is not a human match." }

    $frames = Read-JsonLines (Join-Path $clientDirectory ("presentation-{0}.jsonl" -f $side)) "client-$index presentation"
    $final = $frames[-1]
    if ((Require-Text $final 'Phase' '^MatchResult$' "client-$index final presentation") -cne 'MatchResult') { throw "client-$index does not have a final match result." }
    $wins0 = Require-Integer $final 'Wins0' 0 4 "client-$index final presentation"
    $wins1 = Require-Integer $final 'Wins1' 0 4 "client-$index final presentation"
    if ([Math]::Max($wins0, $wins1) -ne 4) { throw "client-$index final result does not have four wins." }
    $round = Require-Integer $final 'Round' 1 128 "client-$index final presentation"
    $revision = Require-Integer $final 'Revision' 0 2147483647 "client-$index final presentation"
    if ((Require-Property $final 'Pending' "client-$index final presentation") -ne $false) { throw "client-$index final presentation is still pending." }

    $connections = 0
    $generation = 0
    $streams = @()
    $battle = @{}
    foreach ($frame in $frames) {
        $frameConnections = Require-Integer $frame 'Connections' 0 128 "client-$index presentation"
        $frameGeneration = Require-Integer $frame 'AuthGeneration' 0 128 "client-$index presentation"
        $connections = [Math]::Max($connections, $frameConnections)
        $generation = [Math]::Max($generation, $frameGeneration)
        if ($frame.PSObject.Properties.Name -notcontains 'StreamId') { throw "Missing StreamId in client-$index presentation evidence." }
        $streamValue = $frame.StreamId
        $stream = if ($null -eq $streamValue) { '' } else { [string]$streamValue }
        if ($stream.Length -gt 0) {
            if ($stream -notmatch '^[a-f0-9]{32}$') { throw "Invalid StreamId in client-$index presentation evidence." }
            if ($streams -notcontains $stream) { $streams += $stream }
        }
        if ([string]$frame.Phase -eq 'Battle') {
            $frameRound = Require-Integer $frame 'Round' 1 128 "client-$index battle presentation"
            $tick = Require-Integer $frame 'Tick' 0 2147483647 "client-$index battle presentation"
            $hash = Require-Text $frame 'EntitiesHash' '^[A-F0-9]{64}$' "client-$index battle presentation"
            $key = "$frameRound`:$tick"
            if ($battle.ContainsKey($key) -and $battle[$key] -cne $hash) { throw "client-$index has conflicting state at $key." }
            $battle[$key] = $hash
        }
    }
    if ($streams.Count -ne 1) { throw "client-$index did not retain one observed logical stream." }
    $records += [pscustomobject]@{
        Client = "client-$index"; MatchId = $matchId; Side = [int]$side; Wins0 = [int]$wins0; Wins1 = [int]$wins1
        Round = [int]$round; Revision = [int]$revision; Connections = [int]$connections; AuthGeneration = [int]$generation
        StableStream = [string]$streams[0]; Battle = $battle
    }
}

if (@($records | Select-Object -ExpandProperty MatchId -Unique).Count -ne 1) { throw 'Client assignments do not share one match.' }
if (@($records | Select-Object -ExpandProperty Side -Unique).Count -ne 2) { throw 'Client assignments do not hold both sides.' }
$first = $records[0]; $second = $records[1]
if ($first.Wins0 -ne $second.Wins0 -or $first.Wins1 -ne $second.Wins1 -or $first.Round -ne $second.Round -or $first.Revision -ne $second.Revision) {
    throw 'Final authoritative results differ between clients.'
}

$sharedTicks = 0
foreach ($key in $first.Battle.Keys) {
    if ($second.Battle.ContainsKey($key)) {
        if ($first.Battle[$key] -cne $second.Battle[$key]) { throw "Entity state differs at $key." }
        $sharedTicks++
    }
}
if ($sharedTicks -lt 10) { throw 'Fewer than ten shared authoritative battle ticks were observed.' }

$acceptance = [ordered]@{
    Status = 'PASS'; Endpoint = $endpoint.AbsoluteUri; InstanceId = $instanceId; MatchId = $first.MatchId
    Wins0 = $first.Wins0; Wins1 = $first.Wins1; Round = $first.Round; Revision = $first.Revision; Pending = $false
    SharedBattleTicks = $sharedTicks
    Clients = @($records | ForEach-Object { [ordered]@{ Client = $_.Client; Side = $_.Side; Connections = $_.Connections; AuthGeneration = $_.AuthGeneration; StableStream = $_.StableStream } })
}
$acceptance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $remoteRun 'remote-acceptance.json') -Encoding utf8
$acceptance | ConvertTo-Json -Compress
