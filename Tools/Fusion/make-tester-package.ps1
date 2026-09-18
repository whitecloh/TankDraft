param()

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5) { throw 'Run this package tool in Windows PowerShell 5.1.' }

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$systemSid = 'S-1-5-18'
$ownerIdentity = New-Object Security.Principal.SecurityIdentifier($ownerSid)
$systemIdentity = New-Object Security.Principal.SecurityIdentifier($systemSid)

function Assert-PrivatePath([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw 'Required private input is missing.' }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Private input cannot be a link.' }
    foreach ($rule in (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($ownerSid, $systemSid)) { throw 'Private input has broad filesystem access.' }
    }
}

function Assert-NoReparse([string]$Path) {
    $items = @(Get-Item -LiteralPath $Path -Force) + @(Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Package source cannot contain a link.' }
}

function Set-PrivateAcl([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRuleAll($rule) }
    $inheritance = if ((Get-Item -LiteralPath $Path).PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    foreach ($sid in @($ownerIdentity, $systemIdentity)) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', $inheritance, 'None', 'Allow')
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-PhotonToken([string]$Path) {
    Assert-PrivatePath $Path
    $value = (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json).PhotonToken
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -lt 16) { throw 'Private authentication input is invalid.' }
    return [string]$value
}

function Assert-NoForbiddenValue([string]$Directory, [string[]]$Forbidden) {
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Force -Recurse) {
        $ascii = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($file.FullName))
        foreach ($value in $Forbidden) {
            if ($value.Length -ge 16 -and $ascii.Contains($value)) { throw 'Package secret scan failed.' }
        }
    }
}

foreach ($path in @($privateRoot, (Join-Path $privateRoot 'playfab-test-identities.json'), (Join-Path $privateRoot 'fusion-client-1-auth.json'), (Join-Path $privateRoot 'fusion-server-manager.json'))) { Assert-PrivatePath $path }
$testers = @((Get-Content -LiteralPath (Join-Path $privateRoot 'playfab-test-identities.json') -Raw | ConvertFrom-Json))
if ($testers.Count -ne 2 -or @($testers | Select-Object -Unique).Count -ne 2 -or @($testers | Where-Object { $_ -notmatch '^tankdraft-r1-[a-fA-F0-9]{64}$' }).Count) { throw 'Expected exactly two distinct existing tester identities.' }
$testerId = [string]((Get-Content -LiteralPath (Join-Path $privateRoot 'fusion-client-1-auth.json') -Raw | ConvertFrom-Json).UserId)
if ($testerId -notmatch '^[a-fA-F0-9]{5,32}$') { throw 'Tester authentication input is invalid.' }
$manager = Get-Content -LiteralPath (Join-Path $privateRoot 'fusion-server-manager.json') -Raw | ConvertFrom-Json
if ($manager.AllowPlaintextQa -ne $true -or @($manager.AllowlistedAccounts) -notcontains $testerId) { throw 'Tester is not enabled for the configured closed QA manager.' }

$base = 'http://127.0.0.1:18878'
$page = (Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 3).Content
$csrf = [regex]::Match($page, 'name="control-token" content="([A-F0-9]{64})"').Groups[1].Value
if ($csrf -notmatch '^[A-F0-9]{64}$') { throw 'Local manager control token is unavailable.' }
$headers = @{ 'X-TankDraft-Control' = $csrf }
$initialStatus = Invoke-RestMethod -Uri "$base/api/status" -Headers $headers -TimeoutSec 3
if ($initialStatus.State -ne 'Ready' -or $initialStatus.InstanceId -notmatch '^[a-f0-9]{32}$' -or $initialStatus.Stopping -eq $true -or $initialStatus.Authority.Draining -eq $true) { throw 'Local Fusion manager must be Ready and non-draining.' }
$instanceId = [string]$initialStatus.InstanceId

$contentPath = Join-Path $projectRoot 'Backend/Content/local-match.sha256'
if (-not (Test-Path -LiteralPath $contentPath -PathType Leaf)) { throw 'Content version is missing.' }
$contentVersion = (Get-Content -LiteralPath $contentPath -Raw).Trim()
if ($contentVersion -notmatch '^[a-f0-9]{64}$') { throw 'Content version must be a lowercase SHA-256.' }

$clientRoot = Join-Path $projectRoot 'Builds/Fusion/Client'
$templates = Join-Path $projectRoot 'Tools/Fusion/TesterPackage'
$allowed = @('TankDraftFusionClient.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'MonoBleedingEdge', 'D3D12', 'TankDraftFusionClient_Data')
Assert-NoReparse $clientRoot
Assert-NoReparse $templates
foreach ($name in $allowed) {
    $source = Join-Path $clientRoot $name
    if (-not (Test-Path -LiteralPath $source)) { throw 'Required client build input is missing.' }
    Assert-NoReparse $source
}
foreach ($name in @('StartGame.cmd', 'CheckRuntime.cmd', 'Launch.ps1', 'README_RU.txt')) {
    $source = Join-Path $templates $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Tester package template is missing.' }
    Assert-NoReparse $source
}

$privateFiles = @((Join-Path $privateRoot 'playfab-server-key.txt'), (Join-Path $privateRoot 'fusion-server-identity.txt')) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
foreach ($path in $privateFiles) { Assert-PrivatePath $path }
$forbidden = @([string]$testers[0], (Get-Content -LiteralPath (Join-Path $privateRoot 'playfab-server-key.txt') -Raw).Trim(), (Get-PhotonToken (Join-Path $privateRoot 'fusion-client-0-auth.json')), (Get-PhotonToken (Join-Path $privateRoot 'fusion-client-1-auth.json')), (Get-PhotonToken (Join-Path $privateRoot 'fusion-server-auth.json')))
if (Test-Path -LiteralPath (Join-Path $privateRoot 'fusion-server-identity.txt') -PathType Leaf) { $forbidden += (Get-Content -LiteralPath (Join-Path $privateRoot 'fusion-server-identity.txt') -Raw).Trim() }

$distributionRoot = Join-Path $projectRoot 'Builds/Distributions'
New-Item -ItemType Directory -Path $distributionRoot -Force | Out-Null
do { $packageName = 'TankDraft-PC-Tester01-' + (Get-Date -Format 'yyyyMMdd-HHmmss'); $stage = Join-Path $distributionRoot $packageName; if (Test-Path -LiteralPath $stage) { Start-Sleep -Seconds 1 } } while (Test-Path -LiteralPath $stage)
New-Item -ItemType Directory -Path $stage | Out-Null
Set-PrivateAcl $stage
$game = Join-Path $stage 'Game'
New-Item -ItemType Directory -Path $game | Out-Null
foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $clientRoot $name) -Destination $game -Recurse -Force }
foreach ($name in @('StartGame.cmd', 'CheckRuntime.cmd', 'Launch.ps1', 'README_RU.txt')) { Copy-Item -LiteralPath (Join-Path $templates $name) -Destination $stage -Force }
@{ SchemaVersion = 1; TitleId = 'B16D9'; PhotonAppId = '92d5f593-3b30-4493-8168-ca40ae0e55b0'; CustomId = [string]$testers[1]; ExpectedPlayFabId = $testerId; ContentVersion = $contentVersion; PackageId = [Guid]::NewGuid().ToString('N') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'TesterAccess.json') -Encoding UTF8
Set-Content -LiteralPath (Join-Path $stage 'ServerCode.txt') -Value $instanceId -NoNewline -Encoding ASCII
Assert-NoForbiddenValue $stage $forbidden

$finalStatus = Invoke-RestMethod -Uri "$base/api/status" -Headers $headers -TimeoutSec 3
if ($finalStatus.State -ne 'Ready' -or $finalStatus.Stopping -eq $true -or $finalStatus.Authority.Draining -eq $true -or $finalStatus.InstanceId -ne $instanceId) { throw 'Server changed during packaging; package was not created.' }
$zip = Join-Path $distributionRoot ($packageName + '.zip')
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage -Force | ForEach-Object FullName) -DestinationPath $zip -CompressionLevel Optimal
Set-PrivateAcl $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $zip).Length
[PSCustomObject]@{ ArtifactPath = $zip; Size = $size; Sha256 = $hash; TesterIndex = 1 } | ConvertTo-Json -Compress
