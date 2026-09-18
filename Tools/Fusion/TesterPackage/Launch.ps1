param(
    [string]$ServerCode,
    [switch]$CheckRuntime
)

Add-Type -AssemblyName System.Net.Http
$ErrorActionPreference = 'Stop'
$stage = 'initialization'
$logDirectory = 'not created'
$failureHint = 'The tester launcher could not start.'
$runtimeStep = 'none'

function Fail([string]$Message) {
    Write-Host ('Tester launcher failed at {0}: {1}' -f $script:stage, $Message)
    Write-Host ('Logs: {0}' -f $script:logDirectory)
    exit 1
}

function Test-NoReparsePoint([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or [string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        $current = $parent
    }
}

function Set-PrivateAcl([string]$Path) {
    # Fresh SID-only DACL: do not resolve inherited/orphaned account names,
    # or copy an existing owner/group requiring elevated privileges to write.
    $directory = [IO.Directory]::Exists($Path)
    if ($directory) { $acl = New-Object Security.AccessControl.DirectorySecurity }
    else { $acl = New-Object Security.AccessControl.FileSecurity }
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]::None
    if ($directory) { $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit' }
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $rights = [Security.AccessControl.FileSystemRights]::FullControl
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemSid = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
    foreach ($sid in @($currentSid, $systemSid)) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, $rights, $inherit, $propagation, [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    if ($directory) { [IO.Directory]::SetAccessControl($Path, $acl) }
    else { [IO.File]::SetAccessControl($Path, $acl) }
}

function New-PrivateDirectory([string]$Path) {
    $script:runtimeStep = 'check-directory'
    if (-not (Test-NoReparsePoint $Path)) { throw 'unsafe runtime directory' }
    $script:runtimeStep = 'create-directory'
    [void][IO.Directory]::CreateDirectory($Path)
    if (-not (Test-NoReparsePoint $Path)) { throw 'unsafe runtime directory' }
    $script:runtimeStep = 'protect-directory'
    Set-PrivateAcl $Path
}

function Write-PrivateJson([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-NoReparsePoint $parent)) { throw 'unsafe runtime directory' }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    Set-PrivateAcl $Path
}

function Initialize-PrivateRuntime([string]$PackageId) {
    $script:runtimeStep = 'resolve-local-appdata'
    $localRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localRoot) -or -not [IO.Path]::IsPathRooted($localRoot)) { throw 'local appdata unavailable' }
    $ownedRoot = Join-Path $localRoot ('TankDraft\TesterQA\' + $PackageId)
    New-PrivateDirectory $ownedRoot
    $resume = Join-Path $ownedRoot 'resume'
    New-PrivateDirectory $resume
    $run = Join-Path $ownedRoot ([Guid]::NewGuid().ToString('N'))
    New-PrivateDirectory $run
    $logs = Join-Path $run 'Logs'
    New-PrivateDirectory $logs
    $script:runtimeStep = 'write-check'
    $probe = Join-Path $run 'write-check.json'
    Write-PrivateJson $probe @{ Check = 'ok' }
    [IO.File]::Delete($probe)
    return @{ Run = $run; Resume = $resume; Logs = $logs }
}

function Test-FreshStatus([string]$Path, [DateTime]$AfterUtc) {
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
        $item = Get-Item -LiteralPath $Path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.LastWriteTimeUtc -lt $AfterUtc -or ([DateTime]::UtcNow - $item.LastWriteTimeUtc).TotalSeconds -gt 5) { return $false }
        $status = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return $status.Ready -eq $true -and $status.CloudReady -eq $true
    } catch { return $false }
}

function Test-SamePackageClient([string]$ExpectedPath) {
    foreach ($candidate in @(Get-Process -Name 'TankDraftFusionClient' -ErrorAction SilentlyContinue)) {
        try { $candidatePath = $candidate.Path } catch { continue }
        if ($candidatePath -and [string]::Equals([IO.Path]::GetFullPath($candidatePath), $ExpectedPath, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Invoke-PlayFab([System.Net.Http.HttpClient]$Client, [string]$Route, [object]$Body, [string]$SessionTicket) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $Route)
    $response = $null
    if ($SessionTicket) { [void]$request.Headers.TryAddWithoutValidation('X-Authorization', $SessionTicket) }
    $json = $Body | ConvertTo-Json -Compress
    $request.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw 'request rejected' }
        $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($result.code -ne 200) { throw 'provider rejected request' }
        return $result
    } finally { if ($response) { $response.Dispose() }; $request.Dispose() }
}

try {
    if ($CheckRuntime) {
        $stage = 'private runtime setup'
        $failureHint = 'The private tester runtime directory could not be prepared.'
        $runtime = Initialize-PrivateRuntime 'launcher-runtime-check'
        Write-Host 'Private runtime check PASS. No login or game launch performed.'
        exit 0
    }
    $packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    $manifestPath = Join-Path $packageRoot 'TesterAccess.json'
    $serverCodePath = Join-Path $packageRoot 'ServerCode.txt'
    $executable = Join-Path $packageRoot 'Game\TankDraftFusionClient.exe'
    $stage = 'package validation'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) { $failureHint = 'The tester package is incomplete.'; throw 'controlled' }
    $rawManifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($rawManifest -match '(?i)serversecret|staticphotontokens') { $failureHint = 'The tester package is invalid.'; throw 'controlled' }
    $manifest = $rawManifest | ConvertFrom-Json
    $requiredFields = @('SchemaVersion', 'TitleId', 'PhotonAppId', 'CustomId', 'ExpectedPlayFabId', 'ContentVersion', 'PackageId')
    $actualFields = @($manifest.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actualFields.Count -ne $requiredFields.Count -or @($actualFields | Where-Object { $_ -notin $requiredFields }).Count -ne 0) { $failureHint = 'The tester package manifest is invalid.'; throw 'controlled' }
    if ($manifest.SchemaVersion -ne 1 -or $manifest.TitleId -cne 'B16D9' -or $manifest.PhotonAppId -cne '92d5f593-3b30-4493-8168-ca40ae0e55b0' -or
        [string]$manifest.CustomId -notmatch '^tankdraft-r1-[a-fA-F0-9]{64}$' -or [string]$manifest.ExpectedPlayFabId -notmatch '^[a-fA-F0-9]{5,32}$' -or
        [string]$manifest.ContentVersion -notmatch '^[a-f0-9]{64}$' -or [string]$manifest.PackageId -notmatch '^[a-fA-F0-9]{32}$') { $failureHint = 'The tester package manifest is invalid.'; throw 'controlled' }
    $executable = [IO.Path]::GetFullPath($executable)
    if (Test-SamePackageClient $executable) { $failureHint = 'This tester package is already running. Close it before starting another copy.'; throw 'controlled' }

    $stage = 'server code'
    if ([string]::IsNullOrWhiteSpace($ServerCode)) {
        $defaultCode = ''
        if (Test-Path -LiteralPath $serverCodePath -PathType Leaf) { $defaultCode = (Get-Content -LiteralPath $serverCodePath -Raw).Trim() }
        $ServerCode = Read-Host ('Server code [{0}]' -f $defaultCode)
        if ([string]::IsNullOrWhiteSpace($ServerCode)) { $ServerCode = $defaultCode }
    }
    $ServerCode = $ServerCode.Trim()
    if ($ServerCode -match '^(?i:td-qa-)([a-f0-9]{32})$') { $ServerCode = $Matches[1] }
    if ($ServerCode -notmatch '^[a-fA-F0-9]{32}$') { $failureHint = 'The server code is invalid. Use 32 hexadecimal characters.'; throw 'controlled' }
    $sessionName = 'td-qa-' + $ServerCode.ToLowerInvariant()

    $stage = 'private runtime setup'
    $failureHint = 'The private tester runtime directory could not be prepared.'
    $runtime = Initialize-PrivateRuntime ([string]$manifest.PackageId)
    $resumeDirectory = $runtime.Resume
    $runDirectory = $runtime.Run
    $logDirectory = $runtime.Logs

    $stage = 'PlayFab authentication'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false; $handler.UseCookies = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.BaseAddress = New-Object Uri('https://B16D9.playfabapi.com/')
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $failureHint = 'PlayFab login failed. Check the VPN connection and try again.'
        $login = Invoke-PlayFab $client 'Client/LoginWithCustomID' @{ TitleId = 'B16D9'; CustomId = [string]$manifest.CustomId; CreateAccount = $false } $null
        $playFabId = [string]$login.data.PlayFabId
        $ticket = [string]$login.data.SessionTicket
        if ($playFabId -cne [string]$manifest.ExpectedPlayFabId -or [string]::IsNullOrWhiteSpace($ticket) -or $ticket.Length -gt 4096) { $failureHint = 'The tester identity was not accepted.'; throw 'controlled' }
        $failureHint = 'Photon authentication failed. Check the VPN connection and try again.'
        $photon = Invoke-PlayFab $client 'Client/GetPhotonAuthenticationToken' @{ PhotonApplicationId = [string]$manifest.PhotonAppId } $ticket
        $photonToken = [string]$photon.data.PhotonCustomAuthenticationToken
        if ([string]::IsNullOrWhiteSpace($photonToken) -or $photonToken.Length -gt 4096) { $failureHint = 'Photon authentication was not accepted.'; throw 'controlled' }
    } finally { if ($client) { $client.Dispose() }; if ($handler) { $handler.Dispose() } }

    $stage = 'runtime configuration'
    $failureHint = 'The tester runtime configuration could not be prepared.'
    $authPath = Join-Path $runDirectory 'auth.json'
    $gatewayPath = Join-Path $runDirectory 'gateway.json'
    $statusPath = Join-Path $runDirectory 'status.json'
    Write-PrivateJson $authPath @{ UserId = $playFabId; PhotonToken = $photonToken }
    Write-PrivateJson $gatewayPath @{ Role = 'Client'; AuthPath = $authPath; StatusPath = $statusPath; SessionName = $sessionName; LifetimeSeconds = 1800; AllowPlaintextQa = $true; PresentationDirectory = $logDirectory }
    $ticket = $null; $photonToken = $null

    $stage = 'game startup'
    $failureHint = 'The tester game could not be started. Check the log directory.'
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $executable; $start.WorkingDirectory = Split-Path -Parent $executable; $start.UseShellExecute = $false
    $start.Arguments = '-screen-width 540 -screen-height 960 -screen-fullscreen 0 -logFile "{0}"' -f (Join-Path $logDirectory 'player.log')
    foreach ($name in @($start.EnvironmentVariables.Keys | Where-Object { $_ -like 'TANKDRAFT_*' -or $_ -like 'TD_*' })) { $start.EnvironmentVariables.Remove($name) }
    $start.EnvironmentVariables['TANKDRAFT_FUSION_RUNTIME_PATH'] = $gatewayPath
    $start.EnvironmentVariables['TD_FUSION_STATE_DIRECTORY'] = $resumeDirectory
    $startedAt = [DateTime]::UtcNow
    $process = [Diagnostics.Process]::Start($start)
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { $failureHint = 'The game exited before it became ready. Check the log directory.'; throw 'controlled' }
        if (Test-FreshStatus $statusPath $startedAt) {
            Write-Host ('Tester game is ready. Logs: {0}' -f $logDirectory)
            exit 0
        }
        Start-Sleep -Milliseconds 500
    }
    $stage = 'readiness timeout'
    $failureHint = 'The game remains open for inspection but did not become ready within 60 seconds.'
    throw 'controlled'
} catch {
    if ($stage -eq 'private runtime setup') {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        # Only type/code/step; never emit raw errors containing credentials.
        Write-Host ('Runtime diagnostic: step={0}; type={1}; hresult={2}' -f $runtimeStep, $cause.GetType().FullName, $cause.HResult)
        Write-Host 'Use your normal Windows account. Do not run as administrator or disable Windows protection.'
    }
    Fail $failureHint
}
