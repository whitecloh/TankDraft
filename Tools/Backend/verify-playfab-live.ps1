param([switch]$ProvisionTwoTestPlayers)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$secretDirectory = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$keyPath = Join-Path $secretDirectory 'playfab-server-key.txt'
if (-not (Test-Path -LiteralPath $keyPath)) { throw 'Save the PlayFab key in the private .codex-secrets/TankDraft/playfab-server-key.txt file using Notepad. Do not send the key in chat.' }
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$privatePaths = @($secretDirectory, $keyPath)
$identitiesPath = Join-Path $secretDirectory 'playfab-test-identities.json'
if (Test-Path -LiteralPath $identitiesPath) { $privatePaths += $identitiesPath }
foreach ($path in $privatePaths) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Secret paths must not be links.' }
    $acl = Get-Acl -LiteralPath $path
    foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($ownerSid,'S-1-5-18')) { throw 'Private credential path has broad access; fix local ACL before running.' }
    }
}
$project = Join-Path $root 'Backend/TankDraft.PlayFabLiveProbe/TankDraft.PlayFabLiveProbe.csproj'
$dotnet = Join-Path $root 'Logs/BackendSdk/dotnet.exe'
Push-Location (Join-Path $root 'Backend')
try {
    & $dotnet restore $project --configfile NuGet.Config --locked-mode
    if ($LASTEXITCODE) { throw 'Locked live probe restore failed.' }
    $mode = if ($ProvisionTwoTestPlayers) { '--provision-two-test-players' } else { '--login-existing' }
    & $dotnet run --project $project -c Release --no-restore -- $mode
    if ($LASTEXITCODE) { throw 'Live identity probe failed; raw provider response intentionally suppressed.' }
} finally { Pop-Location }
