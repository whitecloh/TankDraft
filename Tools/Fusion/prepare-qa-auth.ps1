param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($path in @($privateRoot, (Join-Path $privateRoot 'playfab-test-identities.json'), (Join-Path $privateRoot 'playfab-server-key.txt'))) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Private credentials must not be links.' }
    foreach ($rule in (Get-Acl -LiteralPath $path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($ownerSid,'S-1-5-18')) { throw 'Private credentials have broad filesystem access.' }
    }
}
$runtime = Join-Path $projectRoot 'Logs/BackendSdk/dotnet.exe'
$env:DOTNET_ROOT = Split-Path -Parent $runtime
$mode = if ($CheckOnly) { '--check-addon' } else { '--prepare-qa' }
& $runtime run --project (Join-Path $projectRoot 'Backend/TankDraft.FusionQaSetup/TankDraft.FusionQaSetup.csproj') -- $mode
if ($LASTEXITCODE) { throw 'Fusion authentication preparation failed; provider payload was suppressed.' }
