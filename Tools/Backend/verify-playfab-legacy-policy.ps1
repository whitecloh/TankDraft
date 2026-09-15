param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false; $handler.UseCookies=$false
$http = [Net.Http.HttpClient]::new($handler); $http.Timeout=[TimeSpan]::FromSeconds(15)
function Call-Qa([string]$Endpoint, [object]$Body, [string]$Header='', [string]$Credential='') {
    if ($Endpoint -notin @('Admin/GetPolicy','Client/LoginWithCustomID','Client/AddUserVirtualCurrency','Client/PurchaseItem','Object/SetObjects','Profile/SetProfilePolicy','Server/GrantItemsToUser')) { throw 'Unexpected QA endpoint.' }
    $req=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,('https://B16D9.playfabapi.com/'+$Endpoint))
    if ($Header) { $req.Headers.Add($Header,$Credential) }
    $req.Content=[Net.Http.StringContent]::new(($Body|ConvertTo-Json -Depth 12 -Compress),[Text.Encoding]::UTF8,'application/json')
    try {
        $resp=$http.SendAsync($req).GetAwaiter().GetResult()
        try { $raw=$resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(); if ($raw.Length -gt 262144) { throw 'Response too large.' }; return ($raw|ConvertFrom-Json) }
        finally { $resp.Dispose() }
    } finally { $req.Dispose() }
}
function Assert-Denied($Result, [string]$Name) {
    if ($Result.error -notin @('APIAccessDenied','NotAuthorized','NotAuthorizedByTitle','InvalidSecretKey','SecretKeyNotFound','EntityTypeNotAllowed','NotAuthenticated')) {
        throw ('Authorization denial not proven for '+$Name)
    }
    [ordered]@{Check=$Name;Denied=$true;Error=$Result.error}
}
try {
    $key=[IO.File]::ReadAllText((Join-Path $privateRoot 'playfab-server-key.txt')).Trim()
    $policy=Call-Qa 'Admin/GetPolicy' @{PolicyName='ApiPolicy'} 'X-SecretKey' $key
    $key=$null
    if ($policy.code -ne 200) { throw 'Cannot verify policy before negative tests.' }
    foreach ($resource in @('Object/SetObjects','Profile/SetProfilePolicy')) {
        if (-not @($policy.data.Statements | Where-Object { $_.Resource -ceq ('pfrn:api--/'+$resource) -and $_.Effect -ceq 'Deny' -and $_.Principal -ceq '{"title_player_account":"*"}' }).Count) { throw 'Apply reviewed entity deny policy first.' }
    }
    foreach ($resource in @('Client/AddUserVirtualCurrency','Client/PurchaseItem')) {
        if (-not @($policy.data.Statements | Where-Object { $_.Resource -ceq ('pfrn:api--/'+$resource) -and $_.Effect -ceq 'Deny' -and $_.Principal -ceq '*' }).Count) { throw 'Apply reviewed client deny policy first.' }
    }
    $identities=@(Get-Content -LiteralPath (Join-Path $privateRoot 'playfab-test-identities.json') -Raw | ConvertFrom-Json)
    $allowlisted=@(([IO.File]::ReadAllText((Join-Path $privateRoot 'remote-test-allowlist.txt')) -split ',') | ForEach-Object { $_.Trim() })
    if ($identities.Count -ne 2 -or @($identities|Select-Object -Unique).Count -ne 2 -or @($identities|Where-Object { $_ -cnotmatch '^tankdraft-r1-[a-f0-9]{64}$' }).Count) { throw 'Existing two QA identities required.' }
    $results=@()
    foreach ($identity in $identities) {
        $login=Call-Qa 'Client/LoginWithCustomID' @{TitleId='B16D9';CustomId=$identity;CreateAccount=$false}
        if ($login.code -ne 200 -or $login.data.NewlyCreated -ne $false -or $login.data.PlayFabId -notin $allowlisted -or $login.data.EntityToken.Entity.Type -ne 'title_player_account') { throw 'Existing allowlisted title player required.' }
        $ticket=$login.data.SessionTicket; $entityToken=$login.data.EntityToken.EntityToken; $entity=$login.data.EntityToken.Entity
        # These syntactically valid requests must be rejected by API policy before execution.
        # A failure stops QA; never use this script before applying the reviewed deny policy.
        $r=Call-Qa 'Object/SetObjects' @{Entity=$entity;Objects=@(@{ObjectName='tankdraft_write_policy_probe';DataObject=@{Probe=$true}})} 'X-EntityToken' $entityToken
        $results+=Assert-Denied $r 'PlayerProfileWrite'
        $r=Call-Qa 'Profile/SetProfilePolicy' @{Entity=$entity;Statements=@(@{Action='*';Effect='Deny';Principal='*';Resource='*'})} 'X-EntityToken' $entityToken
        $results+=Assert-Denied $r 'PlayerProfilePolicyWrite'
        $r=Call-Qa 'Client/AddUserVirtualCurrency' @{VirtualCurrency='CO';Amount=1} 'X-Authorization' $ticket
        $results+=Assert-Denied $r 'PlayerCurrencyGrant'
        $r=Call-Qa 'Client/PurchaseItem' @{CatalogVersion='tankdraft-qa-v1';ItemId='unit.heavy_tank';VirtualCurrency='CO';Price=0} 'X-Authorization' $ticket
        $results+=Assert-Denied $r 'PlayerDirectPurchase'
        $r=Call-Qa 'Server/GrantItemsToUser' @{PlayFabId=$login.data.PlayFabId;CatalogVersion='tankdraft-qa-v1';ItemIds=@('unit.heavy_tank')} 'X-Authorization' $ticket
        $results+=Assert-Denied $r 'PlayerCannotUseServerGrant'
    }
    $report=[ordered]@{TitleId='B16D9';TimeUtc=[DateTime]::UtcNow.ToString('o');QaAccounts=2;Checks=$results;AllDenied=$true}
    $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $PSScriptRoot '../../Logs/MetaLegacy/player-write-policy.json') -Encoding UTF8
    $report|ConvertTo-Json -Depth 8
} catch { throw 'Legacy player permission check failed; credentials and raw responses suppressed. Do not enable meta.' }
finally { $key=$null;$identities=$null;$ticket=$null;$entityToken=$null;$login=$null;$http.Dispose() }
