param([string]$McpUrl = 'http://localhost:21509')
$ErrorActionPreference = 'Stop'
$projectRootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location -LiteralPath $projectRootPath
try {
    New-Item -ItemType Directory -Force -Path Logs/TankDraftSetup | Out-Null
    $verificationCode = @'
using System;
using UnityEditor;
public static class VerifyTypedUi {
    public static object Run() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            throw new Exception("An idle, successfully compiled Editor in Edit Mode is required.");
        try {
            return Type.GetType("TankDraft.Editor.UI.TypedMainMenuValidation, TankDraft.UI.Editor", true)
                .GetMethod("Run").Invoke(null, null);
        } catch (Exception e) { return "FAIL " + e.GetBaseException(); }
    }
}
'@
    @{ className = 'VerifyTypedUi'; methodName = 'Run'; csharpCode = $verificationCode } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath Logs/TankDraftSetup/verify-typed-ui.json -Encoding utf8
    $response = & npx.cmd --yes unity-mcp-cli@0.90.0 run-tool script-execute $projectRootPath --url $McpUrl --input-file Logs/TankDraftSetup/verify-typed-ui.json --raw
    if ($LASTEXITCODE -ne 0) { throw "Unity typed UI verification failed: $response" }
    $response | Set-Content -LiteralPath Logs/TankDraftSetup/typed-ui-verification-result.json -Encoding utf8
    $result = ($response -join "`n") | ConvertFrom-Json
    if ($result.status -ne 'success' -or $result.structured.result.value -notlike 'PASS *') {
        throw "Unity typed UI assertions failed: $response"
    }
    Write-Output $result.structured.result.value
}
finally { Pop-Location }
