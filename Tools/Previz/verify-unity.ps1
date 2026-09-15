param([string]$McpUrl = 'http://localhost:21509')
$ErrorActionPreference = 'Stop'
$projectRootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location -LiteralPath $projectRootPath
try {
    & node Tools/Previz/verify.cjs --export-dir Logs/TankDraftSetup/reference-layouts
    if ($LASTEXITCODE -ne 0) { throw 'Reference layout verification failed.' }
    & node Tools/Previz/verify-groups.cjs --export-dir Logs/TankDraftSetup/group-cases
    if ($LASTEXITCODE -ne 0) { throw 'Group verification failed.' }
    $verificationCode = Get-Content -LiteralPath Tools/Previz/Validation/VerifyGroupedPreviz.cs -Raw
    @{ className = 'VerifyGroupedPreviz'; methodName = 'Run'; csharpCode = $verificationCode } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath Logs/TankDraftSetup/verify-grouped.json -Encoding utf8
    & npx.cmd --yes unity-mcp-cli@0.90.0 run-tool script-execute $projectRootPath --url $McpUrl --input-file Logs/TankDraftSetup/verify-grouped.json --raw
    if ($LASTEXITCODE -ne 0) { throw 'Unity group verification failed.' }
}
finally { Pop-Location }
