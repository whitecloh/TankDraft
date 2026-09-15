param(
    [Parameter(Mandatory)][uri]$Endpoint,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Serial,
    [Parameter(Mandatory)][string]$AdbPath
)

$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $AdbPath -PathType Leaf)) { throw 'Android SDK adb is required.' }
$state = & $AdbPath -s $Serial get-state
if ($LASTEXITCODE -ne 0 -or $state -ne 'device') { throw 'The selected Android device is not ready.' }
$network = & $AdbPath -s $Serial shell dumpsys connectivity
$vpnCount = @($network | Where-Object { $_ -match '^\s{2}NetworkAgentInfo\{network\{\d+\}.*ni\{VPN ' }).Count
$network = $null
if ($vpnCount -lt 1) { throw 'This QA stage requires VPN ON on the phone.' }

# Both helpers validate the Edgegap origin and use only the two existing private
# test identities. They never transmit a server key or enable economy writes.
& (Join-Path $PSScriptRoot 'set-remote-android-bootstrap.ps1') -Serial $Serial -Endpoint $Endpoint.AbsoluteUri -IdentityIndex 1
& $AdbPath -s $Serial shell am force-stop com.tankdraft.remoteqa
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the previous Android QA process.' }
$run = & (Join-Path $PSScriptRoot 'run-remote-clients.ps1') -Endpoint $Endpoint -Players 1 -PassThru
$client = $run.Clients[0]
$log = Join-Path $client.RunDirectory 'player.log'
$started = [Diagnostics.Stopwatch]::StartNew()
$loaded = $false
while ($started.Elapsed.TotalSeconds -lt 60) {
    if (!(Get-Process -Id $client.ProcessId -ErrorAction SilentlyContinue)) { throw 'PC QA process exited before loading its scene. Inspect the run evidence before retrying.' }
    if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'UnloadTime:' -Quiet)) { $loaded = $true; break }
    Start-Sleep -Milliseconds 100
}
if (!$loaded) { throw 'PC scene loading exceeded 60 seconds. The phone was not queued; inspect the PC run before retrying.' }

# The PC auto-queue starts after scene loading. Starting the phone here prevents
# its ten-second search from expiring during the PC's cold assembly load.
& $AdbPath -s $Serial shell am start -n com.tankdraft.remoteqa/com.unity3d.player.UnityPlayerGameActivity --ez tankdraft.remoteAutoQueue true
if ($LASTEXITCODE -ne 0) { throw 'Android launch failed. Inspect the PC queue before retrying.' }
Write-Output "Pair launched after PC scene load. Evidence: $($run.RunDirectory)"
Write-Output 'Keep Wi-Fi and VPN enabled. Matching is confirmed only by identical Human assignments on both clients.'
