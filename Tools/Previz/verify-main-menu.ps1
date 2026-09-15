param([string]$McpUrl = 'http://localhost:21509', [switch]$PlayMode)
$ErrorActionPreference = 'Stop'
$projectRootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location -LiteralPath $projectRootPath
try {
    New-Item -ItemType Directory -Force -Path Logs/TankDraftSetup | Out-Null
    $verificationCode = @'
using System;
using System.IO;
using UnityEditor;
public static class VerifyMainMenu {
    public static object Run() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            return "FAIL: idle, compiled Editor in Edit Mode required.";
        try {
            var structure = Type.GetType("TankDraft.Editor.UI.TypedMainMenuValidation, TankDraft.UI.Editor", true).GetMethod("Run").Invoke(null, null);
            var profile = Type.GetType("TankDraft.Editor.UI.ProfileBehaviorValidation, TankDraft.UI.Editor", true).GetMethod("Run").Invoke(null, null);
            return "PASS\nStructure: " + structure + "\nProfile: " + profile;
        } catch (Exception e) { return "FAIL\n" + e.GetBaseException(); }
    }
}
'@
    if ($PlayMode) {
        $verificationCode = @'
using System;
using System.IO;
using UnityEditor;
public static class VerifyMainMenu {
    public static object Run() {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            return "FAIL: idle, compiled Editor in Edit Mode required.";
        EditorApplication.delayCall += () => {
            try { Type.GetType("TankDraft.Editor.UI.MainMenuPlayModeValidation, TankDraft.UI.Editor", true).GetMethod("Begin").Invoke(null, null); }
            catch (Exception e) {
                Directory.CreateDirectory("Logs/TankDraftSetup/RuntimeQA");
                File.WriteAllText("Logs/TankDraftSetup/RuntimeQA/status.txt", "FAIL\n" + e.GetBaseException());
            }
        };
        return "QUEUED: Play Mode QA; result in Logs/TankDraftSetup/RuntimeQA/status.txt. Close PUN's missing App ID notice with OK if displayed; no network setup is needed for this local test.";
    }
}
'@
    }
    @{ className = 'VerifyMainMenu'; methodName = 'Run'; csharpCode = $verificationCode } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath Logs/TankDraftSetup/verify-main-menu.json -Encoding utf8
    $response = & npx.cmd --yes unity-mcp-cli@0.90.0 run-tool script-execute $projectRootPath --url $McpUrl --input-file Logs/TankDraftSetup/verify-main-menu.json --raw
    $exitCode = $LASTEXITCODE
    $response | Set-Content -LiteralPath Logs/TankDraftSetup/main-menu-verification-result.json -Encoding utf8
    if ($exitCode -ne 0) { throw "Unity verification failed: $response" }
    $result = ($response -join "`n") | ConvertFrom-Json
    $expected = if ($PlayMode) { 'QUEUED*' } else { 'PASS*' }
    if ($result.status -ne 'success' -or $result.structured.result.value -notlike $expected) {
        throw "Unity verification failed: $response"
    }
    Write-Output $result.structured.result.value
}
finally { Pop-Location }
