param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$containerRun = [IO.Path]::GetFullPath($RunDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $containerRun 'remote-container-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Schema -ne 1 -or $manifest.Mode -ne 'NoAzurePrototype' -or $manifest.AutomaticDeploymentAllowed -ne $false -or $manifest.EconomyWritesEnabled -ne $false -or $manifest.Archive.Name -ne 'tankdraft-remote.tar.gz') { throw 'Unexpected container manifest policy.' }
$containerArchive = Join-Path $containerRun 'tankdraft-remote.tar.gz'
if ((Get-FileHash -LiteralPath $containerArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.Archive.Sha256) { throw 'Container hash mismatch.' }
function Read-ContainerJson([string]$Entry) {
    if ($Entry -ne 'index.json' -and $Entry -notmatch '^blobs/sha256/[a-f0-9]{64}$') { throw 'Invalid OCI entry path.' }
    $value = (& tar -xOzf $containerArchive $Entry) -join [Environment]::NewLine
    if ($LASTEXITCODE) { throw 'Cannot read OCI entry.' }
    return ($value | ConvertFrom-Json)
}
$index = Read-ContainerJson 'index.json'
if (@($index.manifests).Count -ne 1 -or $index.manifests[0].digest -notmatch '^sha256:[a-f0-9]{64}$') { throw 'Expected single OCI image.' }
$image = Read-ContainerJson ('blobs/sha256/' + $index.manifests[0].digest.Substring(7))
if ($image.config.digest -notmatch '^sha256:[a-f0-9]{64}$') { throw 'Invalid OCI config reference.' }
$config = Read-ContainerJson ('blobs/sha256/' + $image.config.digest.Substring(7))
$command = @($config.config.Entrypoint) + @($config.config.Cmd | Where-Object { $null -ne $_ })
$entryMode = if ($manifest.EntryMode) { $manifest.EntryMode } else { '--serve' }
if ($entryMode -cnotin @('--serve','--edgegap-managed-tls')) { throw 'Unsupported ingress mode.' }
if ($config.os -ne 'linux' -or $config.architecture -ne 'amd64' -or $config.config.User -ne '1654' -or
    ($command -join '|') -cne ('dotnet|/app/TankDraft.RemoteHost.dll|' + $entryMode) -or $null -eq $config.config.ExposedPorts.'8080/tcp' -or
    $config.config.Labels.'org.opencontainers.image.base.digest' -ne $manifest.BaseImageDigest) { throw 'Unsafe or unexpected OCI runtime configuration.' }
if (@($config.config.Env | Where-Object { $_ -match '^TANKDRAFT_' }).Count -gt 0) { throw 'Runtime configuration or credentials unexpectedly embedded in OCI environment.' }
[ordered]@{Status='PASS';ArchiveHash=$manifest.Archive.Sha256;Platform='linux/amd64';NonRoot=$true;EntryMode=$entryMode;NativeTlsRequired=($entryMode -eq '--serve');ManagedTlsConfigurationRequired=($entryMode -eq '--edgegap-managed-tls');LinuxExecutionVerified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $containerRun 'container-verification.json') -Encoding utf8
'PASS OCI archive hash, non-root runtime, secure entrypoint, no embedded TankDraft credentials. Linux execution not performed.'
