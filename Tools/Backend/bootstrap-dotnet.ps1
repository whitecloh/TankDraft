$ErrorActionPreference = 'Stop'
$backendRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$backendSdkRoot = Join-Path $backendRepoRoot 'Logs/BackendSdk'
$backendSetupRoot = Join-Path $backendRepoRoot 'Logs/BackendSetup'
$backendSdkVersion = '10.0.401'
$backendSdkHash = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
$backendSdkUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$backendSdkVersion/dotnet-sdk-$backendSdkVersion-win-x64.zip"
$backendSdkExe = Join-Path $backendSdkRoot 'dotnet.exe'
if (Test-Path -LiteralPath $backendSdkExe) {
    $backendExistingVersion = & $backendSdkExe --version
    if ($LASTEXITCODE -eq 0 -and $backendExistingVersion -eq $backendSdkVersion) {
        Write-Output "Local SDK ready: $backendSdkVersion"
        exit 0
    }
}
New-Item -ItemType Directory -Force -Path $backendSetupRoot | Out-Null
$backendZip = Join-Path $backendSetupRoot "dotnet-sdk-$backendSdkVersion-win-x64.zip"
& curl.exe --fail --silent --show-error --location --continue-at - --output $backendZip $backendSdkUrl
if ($LASTEXITCODE -ne 0) { throw 'Official SDK download failed.' }
if ((Get-FileHash -LiteralPath $backendZip -Algorithm SHA512).Hash -ne $backendSdkHash) {
    throw 'Official SDK checksum mismatch. No extraction performed.'
}
Expand-Archive -LiteralPath $backendZip -DestinationPath $backendSdkRoot -Force
& $backendSdkExe --version
if ($LASTEXITCODE -ne 0) { throw 'Local SDK verification failed.' }
