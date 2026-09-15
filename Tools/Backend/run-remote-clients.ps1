param(
    [Parameter(Mandatory)][uri]$Endpoint,
    [ValidateRange(1, 2)][int]$Players = 2,
    [Alias('Matches')][ValidateRange(1, 3)][int]$MatchCount = 1,
    [switch]$DisconnectLastMatch,
    [switch]$Manual,
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
if ($Manual -and $MatchCount -ne 1) { throw 'Multiple QA matches require automatic mode.' }
if ($DisconnectLastMatch -and ($Manual -or $MatchCount -lt 2)) { throw 'Disconnect QA requires at least two automatic matches.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($Endpoint.Scheme -cne 'https' -or $Endpoint.DnsSafeHost -cnotmatch '^[a-f0-9]{12,64}\.pr\.edgegap\.net$' -or
    $Endpoint.AbsolutePath -ne '/' -or $Endpoint.UserInfo -or $Endpoint.Query -or $Endpoint.Fragment) { throw 'Expected reviewed Edgegap HTTPS deployment origin.' }
$exe = Join-Path $repo 'Logs/BackendClient/Build/TankDraftServerClient.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build QueueMenu through Unity MCP first.' }
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$identityPath = Join-Path $privateRoot 'playfab-test-identities.json'
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($path in @($privateRoot, $identityPath)) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Private identity path must not be a link.' }
    foreach ($rule in (Get-Acl -LiteralPath $path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($owner,'S-1-5-18')) { throw 'Private identity ACL is too broad.' }
    }
}
$identities = @(Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json)
if ($identities.Count -ne 2 -or @($identities | Where-Object { $_ -cnotmatch '^tankdraft-r1-[a-f0-9]{64}$' }).Count) { throw 'Two existing provisioned test identities are required.' }
$contentVersion = (Get-Content -LiteralPath (Join-Path $repo 'Backend/Content/local-match.sha256') -Raw).Trim()
if ($contentVersion -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid authored content version.' }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false; $handler.UseCookies = $false; $handler.UseProxy = $false
$http = [Net.Http.HttpClient]::new($handler); $http.Timeout = [TimeSpan]::FromSeconds(10)
try {
    $ready = $http.GetAsync([uri]::new($Endpoint,'readyz')).GetAwaiter().GetResult()
    if ([int]$ready.StatusCode -ne 200) { throw 'Remote readiness unavailable.' }
    $body = $ready.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ($body.Length -gt 8192) { throw 'Unexpected readiness response.' }
    $value = $body | ConvertFrom-Json
    $readyFields = @('InstanceId', 'ContentVersion', 'IsDraining', 'EconomyWritesEnabled')
    $actualFields = @($value.PSObject.Properties.Name)
    if ($actualFields.Count -ne $readyFields.Count -or @($actualFields | Where-Object { $readyFields -cnotcontains $_ }).Count) { throw 'Remote readiness field names differ from the client contract.' }
    if ($value.ContentVersion -cne $contentVersion -or $value.InstanceId -cnotmatch '^[a-f0-9]{32}$' -or
        $value.IsDraining -isnot [bool] -or $value.IsDraining -or $value.EconomyWritesEnabled -isnot [bool] -or $value.EconomyWritesEnabled) { throw 'Remote readiness policy rejected.' }
    # This probe contains no identity. TLS certificate validation is always platform-default.
    $plain = [UriBuilder]::new($Endpoint); $plain.Scheme = 'http'; $plain.Port = $Endpoint.Port; $plain.Path = '/readyz'
    $plainAccepted = $false
    try { $response = $http.GetAsync($plain.Uri).GetAwaiter().GetResult(); $plainAccepted = $response.IsSuccessStatusCode; $response.Dispose() } catch { }
    if ($plainAccepted) { throw 'Deployment also accepts public plaintext; do not send player credentials.' }
} finally { $http.Dispose() }
$run = Join-Path $repo ('Logs/RemoteClient/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$started = @()
try {
    for ($index = 0; $index -lt $Players; $index++) {
        $clientRun = Join-Path $run ('client-' + $index)
        [IO.Directory]::CreateDirectory($clientRun) | Out-Null
        $bootstrap = Join-Path $privateRoot ('remote-client-' + [Guid]::NewGuid().ToString('N') + '.json')
        [ordered]@{TitleId='B16D9';CustomId=$identities[$index];BaseUri=$Endpoint.AbsoluteUri;ContentVersion=$contentVersion;RunDirectory=$clientRun} | ConvertTo-Json | Set-Content -LiteralPath $bootstrap -Encoding utf8
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe; $start.WorkingDirectory = $clientRun; $start.UseShellExecute = $false; $start.WindowStyle = 'Hidden'
        foreach ($name in @($start.Environment.Keys | Where-Object { $_ -like 'TD_*' -or $_ -like 'TANKDRAFT_*' })) { [void]$start.Environment.Remove($name) }
        $start.Environment['TD_REMOTE_BOOTSTRAP_PATH'] = $bootstrap
        if (!$Manual) { $start.Environment['TD_REMOTE_AUTO_QUEUE'] = '1'; $start.Environment['TD_LOCAL_AUTO'] = '1'; $start.Environment['TD_REMOTE_AUTO_REMAINING'] = $MatchCount.ToString([Globalization.CultureInfo]::InvariantCulture) }
        if ($DisconnectLastMatch) { $start.Environment['TD_REMOTE_QA_DISCONNECT_LAST'] = '1' }
        foreach ($argument in @('-screen-fullscreen','0','-screen-width','540','-screen-height','960','-logFile',(Join-Path $clientRun 'player.log'))) { [void]$start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        if (!$process) { throw 'Could not start remote test client.' }
        $started += [ordered]@{Index=$index;ProcessId=$process.Id;RunDirectory=$clientRun;PrivateBootstrapPath=$bootstrap}
    }
    [ordered]@{Endpoint=$Endpoint.AbsoluteUri;InstanceId=$value.InstanceId;ContentVersion=$contentVersion;Mode=($(if($Manual){'Manual'}else{'BoundedAuto'}));Matches=$MatchCount;DisconnectLastMatch=[bool]$DisconnectLastMatch;Clients=$started} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'run.json') -Encoding utf8
    if ($PassThru) { [pscustomobject]@{RunDirectory=$run; Clients=$started} }
    else { "Remote clients started. Evidence: $run" }
} catch { throw 'Remote client launch incomplete. Inspect started processes and the remote queue before retrying.' }
finally { $identities = $null }
