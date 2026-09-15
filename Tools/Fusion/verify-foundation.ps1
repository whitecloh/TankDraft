$ErrorActionPreference = 'Stop'
$fusionRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$required = @(
    'Assets/Photon/Fusion/Assemblies/Fusion.Runtime.dll',
    'Assets/Photon/Fusion/Resources/PhotonAppSettings.asset',
    'Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion',
    'Assets/PlayFabSDK',
    'Assets/TankDraft/Runtime/Match/Networking/Core',
    'Backend/TankDraft.Server.PlayFab.Identity',
    'Backend/TankDraft.Server.Match',
    'Backend/TankDraft.Server.Security'
)
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $fusionRoot $relative))) { throw "Required dependency missing: $relative" }
}
$retired = @(
    'Assets/Photon/PhotonUnityNetworking', 'Assets/Photon/FusionDemos', 'Assets/Photon/FusionMenu',
    'Assets/TankDraft/Runtime/Match/Networking/Transport',
    'Assets/TankDraft/Runtime/Match/Networking/Bootstrap',
    'Assets/TankDraft/Editor/Networking', 'Assets/TankDraft/Runtime/Diagnostics/Photon',
    'Backend/TankDraft.Server.Azure', 'Backend/TankDraft.Server.Edgegap',
    'Backend/TankDraft.Server.PlayFab', 'Backend/TankDraft.GsdkLocalProbe', 'Backend/TankDraft.EdgegapHost'
)
foreach ($relative in $retired) {
    if (Test-Path -LiteralPath (Join-Path $fusionRoot $relative)) { throw "Retired dependency still present: $relative" }
}
$projects = Get-ChildItem -LiteralPath (Join-Path $fusionRoot 'Backend') -Directory |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File }
$referenceCount = 0
foreach ($project in $projects) {
    [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
    foreach ($reference in $xml.SelectNodes('//ProjectReference')) {
        $target = [IO.Path]::GetFullPath((Join-Path $project.DirectoryName $reference.Include))
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "Broken project reference in $($project.Name): $($reference.Include)" }
        $referenceCount++
    }
    foreach ($package in $xml.SelectNodes('//PackageReference')) {
        if ($package.Include -match '^(Azure\.|com\.playfab\.csharpgsdk$)') {
            throw "Retired hosting SDK referenced by $($project.Name): $($package.Include)"
        }
    }
}
$config = Get-Content -LiteralPath (Join-Path $fusionRoot 'Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion') -Raw | ConvertFrom-Json
if ($config.EncryptionConfig.EnableEncryption -ne $false -or $config.Simulation.PlayerCount -ne 2) {
    throw 'Current closed QA requires explicit plaintext Fusion configuration and two players per session. Runtime must also opt in with AllowPlaintextQa.'
}
$settings = Get-Content -LiteralPath (Join-Path $fusionRoot 'Assets/Photon/Fusion/Resources/PhotonAppSettings.asset') -Raw
if ($settings -notmatch 'AppIdFusion: 92d5f593-3b30-4493-8168-ca40ae0e55b0') { throw 'Wrong Fusion app.' }
Write-Output "PASS dependency foundation: $($projects.Count) backend projects, $referenceCount references; Fusion config and retired SDKs checked."
Write-Output 'This is not a cloud login, gameplay, reconnect or load test. Run the Unity Fusion Validate Local SDK menu separately.'
