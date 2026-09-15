param([string]$CatalogVersion = 'tankdraft-qa-v1')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
if ($CatalogVersion -cnotmatch '^tankdraft-qa-[a-z0-9-]{1,32}$') { throw 'Only the named QA catalog is allowed.' }
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$outputRoot = Join-Path $PSScriptRoot '../../Logs/MetaLegacy'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$http = [Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(15)
function Read-PlayFab([string]$Endpoint, [object]$Body) {
    if ($Endpoint -notin @('Server/GetCatalogItems','Server/GetUserInventory','Admin/ListVirtualCurrencyTypes','Admin/GetPolicy')) { throw 'Only read operations are allowed.' }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, ('https://B16D9.playfabapi.com/' + $Endpoint))
    $request.Headers.Add('X-SecretKey', $key)
    $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 12 -Compress), [Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        try {
            $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($raw.Length -gt 262144) { throw 'Response exceeds QA limit.' }
            return ($raw | ConvertFrom-Json)
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
try {
    $key = [IO.File]::ReadAllText((Join-Path $privateRoot 'playfab-server-key.txt')).Trim()
    $accounts = @(([IO.File]::ReadAllText((Join-Path $privateRoot 'remote-test-allowlist.txt')) -split ',') | ForEach-Object { $_.Trim() })
    if ($accounts.Count -ne 2 -or @($accounts | Select-Object -Unique).Count -ne 2 -or @($accounts | Where-Object { $_ -cnotmatch '^[A-Fa-f0-9]{5,32}$' }).Count) { throw 'Two existing QA accounts required.' }
    $catalog = Read-PlayFab 'Server/GetCatalogItems' @{CatalogVersion=$CatalogVersion}
    $currencies = Read-PlayFab 'Admin/ListVirtualCurrencyTypes' @{}
    $policy = Read-PlayFab 'Admin/GetPolicy' @{PolicyName='ApiPolicy'}
    $players = @()
    foreach ($account in $accounts) {
        $inventory = Read-PlayFab 'Server/GetUserInventory' @{PlayFabId=$account}
        $players += [ordered]@{Code=$inventory.code;Error=$inventory.error;ItemCount=@($inventory.data.Inventory).Count;Currencies=@($inventory.data.VirtualCurrency.PSObject.Properties.Name)}
    }
    $summary = [ordered]@{TitleId='B16D9';TimeUtc=[DateTime]::UtcNow.ToString('o');ReadOnly=$true;CatalogVersion=$CatalogVersion;CatalogCode=$catalog.code;CatalogError=$catalog.error;CatalogItems=@($catalog.data.Catalog).Count;CurrencyCode=$currencies.code;CurrencyError=$currencies.error;Currencies=@($currencies.data.VirtualCurrencies | Select-Object CurrencyCode,DisplayName,InitialDeposit,RechargeRate,RechargeMax);PolicyCode=$policy.code;PolicyVersion=$policy.data.PolicyVersion;QaPlayers=$players;MutationsApplied=$false}
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'access.json') -Encoding UTF8
    if ($catalog.code -eq 200) { $catalog.data | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $outputRoot 'catalog-before.json') -Encoding UTF8 }
    if ($policy.code -eq 200) { $policy.data | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $outputRoot 'policy-before.json') -Encoding UTF8 }
    $summary | ConvertTo-Json -Depth 10
} catch { throw 'Legacy read-only preflight failed; credentials and raw provider responses suppressed.' }
finally { $key=$null; $accounts=$null; $http.Dispose() }
