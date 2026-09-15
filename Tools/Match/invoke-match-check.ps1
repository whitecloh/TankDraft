param(
    [ValidateSet('Validate','PlayMode','Open','Author')][string]$Action = 'Validate',
    [string]$McpUrl = 'http://localhost:21509'
)
$ErrorActionPreference = 'Stop'
$projectRootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$methods = @{
    Validate = @('MatchValidation', 'Validate')
    PlayMode = @('MatchValidation', 'Begin')
    Open = @('MatchAuthoring', 'Open')
    Author = @('MatchAuthoring', 'Author')
}
$selected = $methods[$Action]
$code = @'
using System;
public static class MatchToolCall {
    public static string Run() {
        try {
            var type = Type.GetType("TankDraft.Match.Editor.CLASS, TankDraft.Match.Editor", true);
            var value = type.GetMethod("METHOD").Invoke(null, null);
            return value == null ? "PASS: ACTION requested." : value.ToString();
        } catch (Exception e) { return "FAIL: " + e.GetBaseException(); }
    }
}
'@
$code = $code.Replace('CLASS', $selected[0]).Replace('METHOD', $selected[1]).Replace('ACTION', $Action)
Push-Location -LiteralPath $projectRootPath
try {
    New-Item -ItemType Directory -Force -Path Logs/TankDraftSetup/MatchQA | Out-Null
    $inputPath = "Logs/TankDraftSetup/MatchQA/$Action-input.json"
    @{className='MatchToolCall';methodName='Run';csharpCode=$code} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $inputPath -Encoding utf8
    $response = & npx.cmd --yes unity-mcp-cli@0.90.0 run-tool script-execute $projectRootPath --url $McpUrl --input-file $inputPath --raw
    if ($LASTEXITCODE -ne 0) { throw 'Unity MCP invocation failed.' }
    $response | Set-Content -LiteralPath "Logs/TankDraftSetup/MatchQA/$Action-result.json" -Encoding utf8
    $result = ($response -join "`n") | ConvertFrom-Json
    if ($result.status -ne 'success' -or $result.structured.result.value -like 'FAIL*') { throw ($response -join "`n") }
    Write-Output $result.structured.result.value
    if ($Action -eq 'PlayMode') { Write-Output 'Final result: Logs/TankDraftSetup/MatchQA/playmode.txt' }
}
finally { Pop-Location }
