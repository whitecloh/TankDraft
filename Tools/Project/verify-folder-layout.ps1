param([string]$McpUrl = 'http://localhost:21509')
$ErrorActionPreference = 'Stop'
$projectRootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location -LiteralPath $projectRootPath
try {
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Validation/FolderLayoutValidation.cs') -Raw
    $inputPath = 'Logs/TankDraftSetup/FolderMigration/project-audit-input.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $inputPath) | Out-Null
    @{className='FolderLayoutValidation'; methodName='Run'; csharpCode=$source} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $inputPath -Encoding utf8
    $response = & npx.cmd --yes unity-mcp-cli@0.90.0 run-tool script-execute $projectRootPath --url $McpUrl --input-file $inputPath --raw
    if ($LASTEXITCODE -ne 0) { throw "Unity MCP audit failed: $response" }
    $result = ($response -join "`n") | ConvertFrom-Json
    if ($result.status -ne 'success' -or $result.structured.result.value -notlike 'PASS*') { throw ($response -join "`n") }
    Write-Output $result.structured.result.value
}
finally { Pop-Location }
