param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$runtimeDirectory = Join-Path $privateRoot 'fusion-editor-run'
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach($path in @($privateRoot, (Join-Path $privateRoot 'playfab-test-identities.json'), (Join-Path $privateRoot 'fusion-server-manager.json'))){
    if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Private input cannot be a link.'}
    foreach($rule in (Get-Acl -LiteralPath $path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($ownerSid,'S-1-5-18')){throw 'Private input has broad filesystem access.'}
    }
}
$base='http://127.0.0.1:18878'
$page=(Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 3).Content
$headers=@{'X-TankDraft-Control'=[regex]::Match($page,'name="control-token" content="([A-F0-9]{64})"').Groups[1].Value}
$status=Invoke-RestMethod "$base/api/status" -Headers $headers -TimeoutSec 3
if($status.state -ne 'Ready' -or $status.instanceId -notmatch '^[a-f0-9]{32}$'){throw 'Start the Fusion server in the panel and wait for Ready.'}
$options=Get-Content -LiteralPath (Join-Path $privateRoot 'fusion-server-manager.json') -Raw|ConvertFrom-Json
if(!$options.AllowPlaintextQa){throw 'Editor gameplay requires the configured closed QA mode.'}
$runtime=Join-Path $projectRoot 'Logs/BackendSdk/dotnet.exe'
$env:DOTNET_ROOT=Split-Path -Parent $runtime
& $runtime run --project (Join-Path $projectRoot 'Backend/TankDraft.FusionQaSetup/TankDraft.FusionQaSetup.csproj') -- --prepare-editor
if($LASTEXITCODE){throw 'Editor authentication preparation failed; provider credentials suppressed.'}
$current=Invoke-RestMethod "$base/api/status" -Headers $headers -TimeoutSec 3
if($current.state -ne 'Ready' -or $current.instanceId -ne $status.instanceId){throw 'Server changed during preparation; retry Play.'}
if((Test-Path -LiteralPath $runtimeDirectory) -and ((Get-Item -LiteralPath $runtimeDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Runtime directory cannot be a link.'}
New-Item -ItemType Directory -Path $runtimeDirectory -Force|Out-Null
$logs=Join-Path $projectRoot ('Logs/FusionEditor/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs -Force|Out-Null
foreach($name in @('stop','disconnect','reconnect-delay-seconds')){Remove-Item -LiteralPath (Join-Path $runtimeDirectory $name) -Force -ErrorAction SilentlyContinue}
@{Role='Client';AuthPath=(Join-Path $privateRoot 'fusion-client-0-auth.json');StatusPath=(Join-Path $runtimeDirectory 'status.json');
    SessionName="td-qa-$($status.instanceId)";LifetimeSeconds=1800;AllowPlaintextQa=$true;PresentationDirectory=$logs}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $runtimeDirectory 'gateway.json') -Encoding UTF8
Write-Output 'PASS Editor client prepared for the running Fusion server.'
