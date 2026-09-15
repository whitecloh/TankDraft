param()
$ErrorActionPreference = 'Stop'
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$path = Join-Path $privateRoot 'playfab-test-identities.json'
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($item in @($privateRoot,$path)) {
    if ((Get-Item -LiteralPath $item).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Private identity must not be a link.' }
    foreach ($rule in (Get-Acl -LiteralPath $item).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @($owner,'S-1-5-18')) { throw 'Identity ACL is too broad.' }
    }
}
$identities = @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
if ($identities.Count -ne 2 -or @($identities | Select-Object -Unique).Count -ne 2 -or @($identities | Where-Object { $_ -cnotmatch '^tankdraft-r1-[a-f0-9]{64}$' }).Count) { throw 'Invalid provisioned identities.' }
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false; $handler.UseCookies=$false
$http = [Net.Http.HttpClient]::new($handler); $http.Timeout=[TimeSpan]::FromSeconds(10)
$accounts=@()
try {
    foreach ($identity in $identities) {
        $body=@{TitleId='B16D9';CustomId=$identity;CreateAccount=$false}|ConvertTo-Json -Compress
        $content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
        $response=$http.PostAsync('https://B16D9.playfabapi.com/Client/LoginWithCustomID',$content).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) { throw 'Existing test login rejected.' }
        $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($text.Length -gt 131072) { throw 'Unexpected identity response.' }
        $data=($text|ConvertFrom-Json).data
        if ($data.NewlyCreated -ne $false -or $data.PlayFabId -cnotmatch '^[A-Fa-f0-9]{5,32}$') { throw 'Existing account required.' }
        $accounts += $data.PlayFabId
        $response.Dispose(); $content.Dispose(); $text=$null; $data=$null
    }
    if (@($accounts|Select-Object -Unique).Count -ne 2) { throw 'Distinct accounts required.' }
    [IO.File]::WriteAllText((Join-Path $privateRoot 'remote-test-allowlist.txt'),($accounts -join ','))
    [ordered]@{Status='ExistingTestAccountsVerified';Count=2;AccountIds=($accounts -join ',');ApiCalls=2;AccountsCreated=0}|ConvertTo-Json -Compress
} catch { throw 'Allowlist preparation failed; credentials and raw provider responses suppressed.' }
finally { $http.Dispose(); $identities=$null; $body=$null; $text=$null; $data=$null }
