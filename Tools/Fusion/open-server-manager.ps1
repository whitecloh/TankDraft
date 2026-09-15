param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not load System.Net.Http on a cold start. Typed
# catch clauses must resolve even when Invoke-WebRequest throws WebException.
Add-Type -AssemblyName System.Net.Http
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$optionsPath = Join-Path $privateRoot 'fusion-server-manager.json'
$runtime = Join-Path $projectRoot 'Logs/BackendSdk/dotnet.exe'
if (-not (Test-Path -LiteralPath $runtime)) { throw 'The local .NET SDK is missing: Logs/BackendSdk/dotnet.exe' }
if (-not (Test-Path -LiteralPath $optionsPath)) {
    $keyPath = Join-Path $privateRoot 'playfab-server-key.txt'
    $allowPath = Join-Path $privateRoot 'remote-test-allowlist.txt'
    if (-not (Test-Path -LiteralPath $keyPath) -or -not (Test-Path -LiteralPath $allowPath)) { throw 'Existing private PlayFab key and tester allowlist are required.' }
    $accounts = @((Get-Content -LiteralPath $allowPath -Raw).Trim() -split '[,\s]+' | Where-Object { $_ })
    if ($accounts.Count -lt 1 -or $accounts.Count -gt 4) { throw 'Expected 1..4 existing testers.' }
    @{ Workspace = $projectRoot; PlayFabSecretPath = $keyPath; AllowlistedAccounts = $accounts } | ConvertTo-Json | Set-Content -LiteralPath $optionsPath -Encoding UTF8
}
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($path in @($privateRoot, $optionsPath)) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Private configuration must not be a link.' }
    foreach ($rule in (Get-Acl -LiteralPath $path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($ownerSid,'S-1-5-18')) { throw 'Private configuration has broad filesystem access.' }
    }
}
try {
    $current = Invoke-WebRequest -Uri 'http://127.0.0.1:18878/' -UseBasicParsing -TimeoutSec 2
    if ($current.Content -notmatch 'TANKDRAFT / LOCAL OPERATIONS') { throw 'The manager port is occupied by another application.' }
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:18878/' }
    return
} catch [System.Net.WebException] { }
catch [System.Net.Http.HttpRequestException] { }
catch [System.OperationCanceledException] { }
$env:DOTNET_ROOT = Split-Path -Parent $runtime
$output = Join-Path $projectRoot 'Builds/Fusion/Manager'
& $runtime publish (Join-Path $projectRoot 'Backend/TankDraft.ServerManager/TankDraft.ServerManager.csproj') -c Release --nologo -o $output
if ($LASTEXITCODE) { throw 'Server Manager build failed.' }
$logs = Join-Path $projectRoot 'Logs/FusionServer'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$dll = Join-Path $output 'TankDraft.ServerManager.dll'
$process = Start-Process -FilePath $runtime -ArgumentList @(('"{0}"' -f $dll)) -WorkingDirectory $output -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'panel-stdout.log') -RedirectStandardError (Join-Path $logs 'panel-stderr.log')
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    if ($process.HasExited) { throw 'Server Manager stopped. See Logs/FusionServer/panel-stderr.log.' }
    try { $response = Invoke-WebRequest -Uri 'http://127.0.0.1:18878/' -UseBasicParsing -TimeoutSec 1; if ($response.StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 250 }
}
if ($attempt -eq 30) { throw 'Server Manager startup timed out.' }
if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:18878/' }
Write-Host 'Server Manager ready: http://127.0.0.1:18878/ (server starts only from the panel)'
