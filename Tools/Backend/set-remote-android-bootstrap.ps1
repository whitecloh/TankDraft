param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._:-]{1,80}$')][string]$Serial,
    [Parameter(Mandatory)][string]$Endpoint,
    [ValidateRange(0, 1)][int]$IdentityIndex = 1
)

$ErrorActionPreference = 'Stop'

$adb = 'U:\UNITY\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
$package = 'com.tankdraft.remoteqa'
$contentPath = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))) 'Backend/Content/local-match.sha256'

function Invoke-Adb([string[]]$Arguments, [string]$StandardInput = $null) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $adb
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'ADB command failed.' }
    try {
        if ($null -ne $StandardInput) {
            $process.StandardInput.Write($StandardInput)
            $process.StandardInput.Flush()
        }
        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(20000)) { $process.Kill(); throw 'ADB command timed out.' }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        [void]$stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'ADB command failed.' }
        return $stdout
    } finally {
        $process.Dispose()
    }
}

function Test-PrivateAcl([string]$Path, [string]$Owner) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Private identity path rejected.' }
    foreach ($rule in (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($Owner, 'S-1-5-18')) {
            throw 'Private identity ACL rejected.'
        }
    }
}

try {
    if (!(Test-Path -LiteralPath $adb -PathType Leaf)) { throw 'Required Android platform tools are unavailable.' }
    try { $endpointUri = [uri]$Endpoint } catch { throw 'Invalid deployment endpoint.' }
    if ($Endpoint -cnotmatch '^https://[a-f0-9]{12,64}\.pr\.edgegap\.net(?::[1-9][0-9]{0,4})?/?$' -or
        $endpointUri.Scheme -cne 'https' -or $endpointUri.DnsSafeHost -cnotmatch '^[a-f0-9]{12,64}\.pr\.edgegap\.net$' -or
        $endpointUri.AbsolutePath -cne '/' -or $endpointUri.UserInfo -or $endpointUri.Query -or $endpointUri.Fragment -or
        $endpointUri.Port -lt 1 -or $endpointUri.Port -gt 65535) {
        throw 'Invalid deployment endpoint.'
    }

    $contentVersion = (Get-Content -LiteralPath $contentPath -Raw).Trim()
    if ($contentVersion -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid authored content version.' }

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseProxy = $false
    $http = [Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromSeconds(10)
    try {
        $readyResponse = $http.GetAsync([uri]::new($endpointUri, 'readyz')).GetAwaiter().GetResult()
        try {
            if ([int]$readyResponse.StatusCode -ne 200) { throw 'Remote readiness rejected.' }
            $readyBody = $readyResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($readyBody.Length -gt 8192) { throw 'Remote readiness rejected.' }
            $ready = $readyBody | ConvertFrom-Json
            $requiredFields = @('InstanceId', 'ContentVersion', 'IsDraining', 'EconomyWritesEnabled')
            $actualFields = @($ready.PSObject.Properties.Name)
            if ($actualFields.Count -ne 4 -or @($actualFields | Where-Object { $requiredFields -cnotcontains $_ }).Count -ne 0 -or
                $ready.InstanceId -cnotmatch '^[a-f0-9]{32}$' -or $ready.ContentVersion -cne $contentVersion -or
                $ready.IsDraining -isnot [bool] -or $ready.IsDraining -or
                $ready.EconomyWritesEnabled -isnot [bool] -or $ready.EconomyWritesEnabled) {
                throw 'Remote readiness rejected.'
            }
        } finally { $readyResponse.Dispose() }

        $plain = [UriBuilder]::new($endpointUri)
        $plain.Scheme = 'http'
        $plain.Port = $endpointUri.Port
        $plain.Path = '/readyz'
        $plain.Query = ''
        $plain.Fragment = ''
        $plainAccepted = $false
        try {
            $plainResponse = $http.GetAsync($plain.Uri).GetAwaiter().GetResult()
            try { $plainAccepted = $plainResponse.IsSuccessStatusCode } finally { $plainResponse.Dispose() }
        } catch { }
        if ($plainAccepted) { throw 'Plaintext endpoint rejected.' }
    } finally {
        $http.Dispose()
        $handler.Dispose()
    }

    $privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
    $identityPath = Join-Path $privateRoot 'playfab-test-identities.json'
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Test-PrivateAcl $privateRoot $owner
    Test-PrivateAcl $identityPath $owner
    $identities = @(Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json)
    if ($identities.Count -ne 2 -or @($identities | Where-Object { $_ -cnotmatch '^tankdraft-r1-[a-f0-9]{64}$' }).Count -ne 0) {
        throw 'Provisioned identities rejected.'
    }

    $bootstrap = [ordered]@{
        TitleId = 'B16D9'
        CustomId = [string]$identities[$IdentityIndex]
        BaseUri = $endpointUri.AbsoluteUri
        ContentVersion = $contentVersion
    } | ConvertTo-Json -Compress
    $bootstrapBytes = [Text.UTF8Encoding]::new($false).GetByteCount($bootstrap)

    if ((Invoke-Adb @('-s', $Serial, 'get-state')).Trim() -cne 'device') { throw 'Android device rejected.' }
    if ((Invoke-Adb @('-s', $Serial, 'shell', 'pm', 'path', $package)).Trim() -cnotmatch '^package:') { throw 'Android package rejected.' }
    [void](Invoke-Adb @('-s', $Serial, 'shell', 'run-as', $package, 'id'))
    $reverse = Invoke-Adb @('-s', $Serial, 'reverse', '--list')
    if ($reverse -match '(?m)(^|\s)tcp:18783(\s|$)') { throw 'Existing game reverse mapping rejected.' }
    [void](Invoke-Adb @('-s', $Serial, 'shell', 'am', 'force-stop', $package))
    [void](Invoke-Adb @('-s', $Serial, 'shell', '-T', "run-as com.tankdraft.remoteqa sh -c 'umask 077; mkdir -p no_backup/remote-evidence && cat > no_backup/td-remote-bootstrap.json.tmp && chmod 600 no_backup/td-remote-bootstrap.json.tmp && mv -f no_backup/td-remote-bootstrap.json.tmp no_backup/td-remote-bootstrap.json'") $bootstrap)
    $writtenBytes = (Invoke-Adb @('-s', $Serial, 'shell', "run-as com.tankdraft.remoteqa sh -c 'wc -c < no_backup/td-remote-bootstrap.json'")).Trim()
    if ($writtenBytes -cnotmatch '^[0-9]+$' -or [long]$writtenBytes -ne $bootstrapBytes) { throw 'Android bootstrap verification failed.' }
    'SUCCESS'
} catch {
    throw 'Android remote bootstrap installation failed.'
} finally {
    $identities = $null
    $bootstrap = $null
    $readyBody = $null
}
