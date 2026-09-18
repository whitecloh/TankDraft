# Offline Windows PowerShell 5.1 regression: no identities, provider calls or game.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1.' }
$launcher = Join-Path $PSScriptRoot 'TesterPackage/Launch.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Launcher syntax error.' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
# Deliberately remove Security cmdlets; runtime setup must not rely on their autoload.
Import-Module Microsoft.PowerShell.Management
Import-Module Microsoft.PowerShell.Utility
Remove-Module Microsoft.PowerShell.Security -ErrorAction SilentlyContinue
$PSModuleAutoLoadingPreference = 'None'
$env:PSModulePath = ''
if (Get-Command Get-Acl -ErrorAction SilentlyContinue) { throw 'Security module is unexpectedly loaded.' }
$probe = Join-Path ([IO.Path]::GetTempPath()) ('TankDraft [ACL] ' + [char]0x416 + ' ' + [Guid]::NewGuid().ToString('N'))
New-PrivateDirectory $probe
New-PrivateDirectory $probe
$file = Join-Path $probe 'probe.json'
Write-PrivateJson $file @{ Check = 'ok' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($path in @($probe, $file)) {
    $acl = if ([IO.Directory]::Exists($path)) { [IO.Directory]::GetAccessControl($path) } else { [IO.File]::GetAccessControl($path) }
    if (-not $acl.AreAccessRulesProtected) { throw 'Runtime ACL is not protected.' }
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne 2) { throw 'Unexpected runtime ACL rule count.' }
    foreach ($rule in $rules) {
        if ($rule.IdentityReference.Value -notin @($sid, 'S-1-5-18') -or $rule.AccessControlType -ne 'Allow') { throw 'Unexpected runtime ACL principal.' }
    }
}
# Inherited/unknown SID entries must not be translated or retained.
$old = [IO.Directory]::GetAccessControl($probe)
$orphan = New-Object Security.Principal.SecurityIdentifier('S-1-5-21-111111111-222222222-333333333-9999')
$old.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($orphan, 'Read', 'Allow')))
[IO.Directory]::SetAccessControl($probe, $old)
New-PrivateDirectory $probe
if (@([IO.Directory]::GetAccessControl($probe).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])).Count -ne 2) { throw 'Orphan SID retained.' }
$env:LOCALAPPDATA = ''
$runtime = Initialize-PrivateRuntime 'launcher-runtime-check'
if (-not [IO.Directory]::Exists($runtime.Logs)) { throw 'Known-folder fallback failed.' }
Write-Output 'PASS: cold runtime, Security cmdlets unavailable, repeated directory, Unicode/bracket path, SID-only protected file/directory ACL, orphan SID replacement, missing LOCALAPPDATA.'
