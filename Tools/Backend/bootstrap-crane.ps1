$ErrorActionPreference = 'Stop'
$toolRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Logs/ContainerTools/crane-v0.22.1'))
$archive = Join-Path $toolRoot 'go-containerregistry_Windows_x86_64.tar.gz'
$expected = '0e073ea8192c3b8442ec8aaf44d53c1050a09084669fae3a6ceb0f2026cf8b21'
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://github.com/google/go-containerregistry/releases/download/v0.22.1/go-containerregistry_Windows_x86_64.tar.gz' -OutFile $archive -TimeoutSec 120
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Official crane release hash mismatch.' }
& tar -xzf $archive -C $toolRoot crane.exe LICENSE
if ($LASTEXITCODE -ne 0) { throw 'Could not extract verified crane release.' }
& (Join-Path $toolRoot 'crane.exe') version
if ($LASTEXITCODE -ne 0) { throw 'Crane bootstrap failed.' }
