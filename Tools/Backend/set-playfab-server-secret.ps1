$ErrorActionPreference = 'Stop'
# Interactive local entry: never put the key in shell arguments, chat, source or logs.
$secretDirectory = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$secretPath = Join-Path $secretDirectory 'playfab-server-key.txt'
[IO.Directory]::CreateDirectory($secretDirectory) | Out-Null
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$directoryAcl = [Security.AccessControl.DirectorySecurity]::new()
$directoryAcl.SetOwner($ownerSid)
$directoryAcl.SetAccessRuleProtection($true, $false)
foreach ($sid in @($ownerSid,$systemSid)) {
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
}
Set-Acl -LiteralPath $secretDirectory -AclObject $directoryAcl
if (Test-Path -LiteralPath $secretPath) { throw 'Key file already exists; update it explicitly outside this first-time setup.' }
$secretValue = Read-Host 'PlayFab B16D9 > Settings > Secret Keys: paste existing key (hidden)' -AsSecureString
$pointer = [IntPtr]::Zero
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secretValue)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ($plain -notmatch '^[\x21-\x7E]{8,512}$') { throw 'Invalid key format; nothing saved.' }
    # File inherits the private directory ACL at creation.
    $stream = [IO.File]::Open($secretPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes($plain); $stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose(); if ($bytes) { [Array]::Clear($bytes,0,$bytes.Length) } }
    Write-Host 'Saved privately for local PlayFab verification. Key was not printed.'
} finally {
    $plain = $null
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secretValue.Dispose()
}
