[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$contentRoot = Join-Path $projectRoot 'Backend/Content'
$rulesPath = Join-Path $contentRoot 'meta-rules.json'
$rulesHashPath = Join-Path $contentRoot 'meta-rules.sha256'
$reviewPath = Join-Path $contentRoot 'meta-legacy-provisioning-review.json'
$privateRoot = Join-Path $env:USERPROFILE '.codex-secrets/TankDraft'
$privateLogRoot = Join-Path $privateRoot 'Logs/MetaLegacy'
$titleId = 'B16D9'
$catalogVersion = 'tankdraft-qa-v1'
$currencyCodes = @('CO', 'GM', 'EN')
$classicWrites = @('AddUserVirtualCurrency','SubtractUserVirtualCurrency','PurchaseItem','StartPurchase','PayForPurchase','ConfirmPurchase','ConsumeItem','UnlockContainerItem','UnlockContainerInstance','RedeemCoupon','OpenTrade','AcceptTrade','CancelTrade','ExecuteCloudScript','ValidateGooglePlayPurchase','ValidateIOSReceipt','ValidateAmazonIAPReceipt','ValidateWindowsStoreReceipt')

function Stop-Safe([string]$Message) { throw $Message }
function Read-Json([string]$Path) { if (-not (Test-Path -LiteralPath $Path)) { Stop-Safe 'Required authored input is missing.' }; return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json) }
function Get-ExpectedHash([string]$Path) { return ((Get-Content -Raw -LiteralPath $Path).Trim() -split '\s+')[0].ToUpperInvariant() }
function Get-StringHash([string]$Value) { $bytes = [Text.Encoding]::UTF8.GetBytes($Value); $sha=[Security.Cryptography.SHA256]::Create(); try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')).Substring(0,16) } finally { $sha.Dispose(); [Array]::Clear($bytes, 0, $bytes.Length) } }
function Write-SafeSummary([hashtable]$Summary) { $Summary | ConvertTo-Json -Depth 8 | Write-Output }
function Test-ApiSuccess([object]$Response, [string]$Operation) { if ($null -eq $Response -or $Response.code -ne 200 -or $Response.error) { $script:failureCode=[int]$Response.errorCode; Stop-Safe ("PlayFab {0} did not succeed; provider details suppressed." -f $Operation) } }
function Invoke-PlayFab([string]$Endpoint, [object]$Body) {
    $allowed = @('Server/GetCatalogItems','Server/GetUserInventory','Admin/ListVirtualCurrencyTypes','Admin/GetPolicy','Admin/UpdatePolicy','Admin/UpdateCatalogItems','Admin/AddVirtualCurrencyTypes','Server/GrantItemsToUser')
    if ($Endpoint -notin $allowed) { Stop-Safe 'Endpoint is outside the Legacy QA allowlist.' }
    $script:stage=$Endpoint
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, ('https://' + $titleId + '.playfabapi.com/' + $Endpoint))
    $request.Headers.Add('X-SecretKey', $script:key)
    $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 20 -Compress), [Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $script:http.SendAsync($request).GetAwaiter().GetResult()
        try { $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); if ($raw.Length -gt 262144) { Stop-Safe 'Provider response exceeded the safety limit.' }; return ($raw | ConvertFrom-Json) }
        finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Get-DenyStatements() {
    $statements = @()
    foreach ($principal in @('{"title_player_account":"*"}','{"master_player_account":"*"}')) {
        foreach ($resource in @('pfrn:api--/Object/SetObjects','pfrn:api--/Profile/SetProfilePolicy','pfrn:api--/Inventory/*')) {
            $statements += [ordered]@{ Resource=$resource; Action='*'; Effect='Deny'; Principal=$principal; Comment='TankDraft Legacy QA server-owned player data' }
        }
    }
    foreach ($action in $classicWrites) { $statements += [ordered]@{ Resource=('pfrn:api--/Client/' + $action); Action='*'; Effect='Deny'; Principal='*'; Comment='TankDraft Legacy QA blocks classic client economy writes' } }
    return $statements
}
function Get-StatementKey([object]$Statement) { return ('{0}|{1}|{2}|{3}' -f $Statement.Resource,$Statement.Action,$Statement.Effect,$Statement.Principal) }
function Test-RequiredDenies([object[]]$Statements, [object[]]$Required) { $keys = @($Statements | ForEach-Object { Get-StatementKey $_ }); return @($Required | Where-Object { (Get-StatementKey $_) -notin $keys }).Count -eq 0 }
function Get-Accounts() {
    $allowPath = Join-Path $privateRoot 'remote-test-allowlist.txt'
    $accounts = @(([IO.File]::ReadAllText($allowPath) -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($accounts.Count -ne 2 -or @($accounts | Select-Object -Unique).Count -ne 2 -or @($accounts | Where-Object { $_ -cnotmatch '^[A-Fa-f0-9]{5,32}$' }).Count) { Stop-Safe 'Exactly two existing QA identities are required.' }
    return $accounts
}

$rules = Read-Json $rulesPath
$review = Read-Json $reviewPath
$expectedHash = Get-ExpectedHash $rulesHashPath
$actualHash = (Get-FileHash -LiteralPath $rulesPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($expectedHash -ne $actualHash -or [string]::IsNullOrWhiteSpace($rules.ContentVersion)) { Stop-Safe 'Authored meta-rules hash does not match.' }
if ($review.TitleId -ne $titleId -or $review.Mode -ne 'PlayFabLegacyClosedQa' -or $review.Apply -ne $false -or $review.Catalog.Count -ne 24 -or $review.StarterInventory.Count -ne 9 -or @($review.CurrencyGrants).Count -ne 0 -or @($review.RealMoneyProducts).Count -ne 0) { Stop-Safe 'Provisioning review manifest is not the approved closed-QA shape.' }
$definitionIds = @($rules.Definitions | ForEach-Object { $_.Id })
$catalogIds = @($review.Catalog | ForEach-Object { $_.ContentId })
if (@($definitionIds | Where-Object { $_ -notin $catalogIds }).Count -ne 0 -or @($catalogIds | Where-Object { $_ -notin $definitionIds }).Count -ne 0 -or @($catalogIds | Select-Object -Unique).Count -ne 24) { Stop-Safe 'Catalog bindings do not exactly match authored content IDs.' }
$starterIds = @($review.StarterInventory | ForEach-Object { $_.ContentId })
if (@($starterIds | Where-Object { $_ -notin $catalogIds }).Count -ne 0 -or @($starterIds | Select-Object -Unique).Count -ne 9 -or @($review.StarterInventory | Where-Object { $_.Amount -ne 1 }).Count -ne 0) { Stop-Safe 'Starter inventory is invalid.' }
$catalogItems = @($review.Catalog | ForEach-Object { [ordered]@{ ItemId=$_.ContentId; DisplayName=$_.ContentId; CatalogVersion=$catalogVersion; VirtualCurrencyPrices=@{}; CanBecomeCharacter=$false; IsStackable=$false; IsTradable=$false; IsLimitedEdition=$false; IsRefundable=$false; Tags=@('TankDraft','LegacyQa','Durable') } })
$manifest = [ordered]@{ TitleId=$titleId; Mode='PlayFabLegacyClosedQa'; Apply=[bool]$Apply; CatalogVersion=$catalogVersion; MetaRulesHash=$actualHash; CatalogItemCount=$catalogItems.Count; StarterItemCount=$starterIds.Count; CurrencyCodes=$currencyCodes; PolicyDenyCount=(Get-DenyStatements).Count; PlayerWritePolicyVerified=$false }
if (-not $Apply) { Write-SafeSummary $manifest; return }

if (-not (Test-Path -LiteralPath $privateRoot)) { Stop-Safe 'Private TankDraft secrets directory is required.' }
[IO.Directory]::CreateDirectory($privateLogRoot) | Out-Null
$script:key = [IO.File]::ReadAllText((Join-Path $privateRoot 'playfab-server-key.txt')).Trim()
if ([string]::IsNullOrWhiteSpace($script:key)) { Stop-Safe 'Private PlayFab key is required.' }
$accounts = Get-Accounts
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false; $handler.UseCookies = $false
$script:http = [Net.Http.HttpClient]::new($handler); $script:http.Timeout = [TimeSpan]::FromSeconds(15)
try {
    $policyBefore = Invoke-PlayFab 'Admin/GetPolicy' @{ PolicyName='ApiPolicy' }; Test-ApiSuccess $policyBefore 'GetPolicy'
    if (@($policyBefore.data.Warnings | Where-Object { $_ }).Count -ne 0) { Stop-Safe 'Existing ApiPolicy contains warnings; no policy mutation was attempted.' }
    $requiredDenies = Get-DenyStatements
    $existingStatements = @($policyBefore.data.Statements)
    $missingDenies = @($requiredDenies | Where-Object { (Get-StatementKey $_) -notin @($existingStatements | ForEach-Object { Get-StatementKey $_ }) })
    if ($missingDenies.Count -gt 0) {
        $update = Invoke-PlayFab 'Admin/UpdatePolicy' @{ PolicyName='ApiPolicy'; PolicyVersion=$policyBefore.data.PolicyVersion; OverwritePolicy=$false; Statements=@($missingDenies) }; Test-ApiSuccess $update 'UpdatePolicy'
        if (@($update.data.Warnings | Where-Object { $_ }).Count -ne 0) { Stop-Safe 'ApiPolicy update returned warnings.' }
    }
    $policyAfter = Invoke-PlayFab 'Admin/GetPolicy' @{ PolicyName='ApiPolicy' }; Test-ApiSuccess $policyAfter 'GetPolicy after update'
    if (@($policyAfter.data.Warnings | Where-Object { $_ }).Count -ne 0 -or -not (Test-RequiredDenies @($policyAfter.data.Statements) $requiredDenies)) { Stop-Safe 'ApiPolicy verification failed; no catalog or grants were attempted.' }
    $catalogBefore = Invoke-PlayFab 'Server/GetCatalogItems' @{ CatalogVersion=$catalogVersion }; Test-ApiSuccess $catalogBefore 'GetCatalogItems'
    $existingCatalog = @($catalogBefore.data.Catalog)
    $mismatches = @($existingCatalog | Where-Object { $_.ItemId -in $catalogIds -and ($_.CanBecomeCharacter -or $_.IsStackable -or $_.IsTradable -or $_.IsLimitedEdition -or $_.IsRefundable -or $_.Consumable -or $_.Bundle -or $_.Container -or @($_.VirtualCurrencyPrices.PSObject.Properties | Where-Object { $_ }).Count -gt 0 -or @($_.RealCurrencyPrices.PSObject.Properties | Where-Object { $_ }).Count -gt 0) })
    if ($mismatches.Count -gt 0) { Stop-Safe 'Existing catalog item conflicts with the closed-QA contract.' }
    $missingItems = @($catalogItems | Where-Object { $_.ItemId -notin @($existingCatalog | ForEach-Object { $_.ItemId }) })
    if ($missingItems.Count -gt 0) { $result = Invoke-PlayFab 'Admin/UpdateCatalogItems' @{ CatalogVersion=$catalogVersion; SetAsDefaultCatalog=$false; Catalog=@($missingItems) }; Test-ApiSuccess $result 'UpdateCatalogItems' }
    $currenciesBefore = Invoke-PlayFab 'Admin/ListVirtualCurrencyTypes' @{}; Test-ApiSuccess $currenciesBefore 'ListVirtualCurrencyTypes'
    $existingCurrencyCodes = @($currenciesBefore.data.VirtualCurrencies | ForEach-Object { $_.CurrencyCode })
    $currencyConflicts = @($currenciesBefore.data.VirtualCurrencies | Where-Object { $_.CurrencyCode -in $currencyCodes -and ($_.InitialDeposit -ne 0 -or $_.RechargeRate -ne 0) })
    if ($currencyConflicts.Count -gt 0) { Stop-Safe 'Existing virtual currency conflicts with the zero-balance QA contract.' }
    $missingCurrencies = @($currencyCodes | Where-Object { $_ -notin $existingCurrencyCodes } | ForEach-Object { [ordered]@{ CurrencyCode=$_; DisplayName=$_; InitialDeposit=0; RechargeRate=0; RechargeMax=0 } })
    if ($missingCurrencies.Count -gt 0) { $result = Invoke-PlayFab 'Admin/AddVirtualCurrencyTypes' @{ VirtualCurrencies=@($missingCurrencies) }; Test-ApiSuccess $result 'AddVirtualCurrencyTypes' }
    foreach ($account in $accounts) {
        $inventory = Invoke-PlayFab 'Server/GetUserInventory' @{ PlayFabId=$account }; Test-ApiSuccess $inventory 'GetUserInventory before grant'
        $owned = @($inventory.data.Inventory | Where-Object { $_.CatalogVersion -ceq $catalogVersion } | ForEach-Object { $_.ItemId })
        $grant = @($starterIds | Where-Object { $_ -notin $owned })
        if ($grant.Count -eq 0) { continue }
        $markerPath = Join-Path $privateLogRoot ('grant-' + (Get-StringHash $account) + '.json')
        if (Test-Path -LiteralPath $markerPath) { Stop-Safe 'A prior grant intent requires explicit resolution before retry.' }
        [ordered]@{ State='Pending'; TimeUtc=[DateTime]::UtcNow.ToString('o'); AccountHash=(Get-StringHash $account); RequestedCount=$grant.Count } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
        $result = Invoke-PlayFab 'Server/GrantItemsToUser' @{ PlayFabId=$account; CatalogVersion=$catalogVersion; ItemIds=@($grant) }; Test-ApiSuccess $result 'GrantItemsToUser'
        $verify = Invoke-PlayFab 'Server/GetUserInventory' @{ PlayFabId=$account }; Test-ApiSuccess $verify 'GetUserInventory after grant'
        if (@($starterIds | Where-Object { $_ -notin @($verify.data.Inventory | Where-Object { $_.CatalogVersion -ceq $catalogVersion } | ForEach-Object { $_.ItemId }) }).Count -ne 0) { Stop-Safe 'Starter grant verification failed; marker remains pending.' }
        [ordered]@{ State='Verified'; TimeUtc=[DateTime]::UtcNow.ToString('o'); AccountHash=(Get-StringHash $account); GrantedCount=$grant.Count } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
    }
    $catalogAfter = Invoke-PlayFab 'Server/GetCatalogItems' @{ CatalogVersion=$catalogVersion }; Test-ApiSuccess $catalogAfter 'GetCatalogItems after update'
    if (@($catalogIds | Where-Object { $_ -notin @($catalogAfter.data.Catalog | ForEach-Object { $_.ItemId }) }).Count -ne 0) { Stop-Safe 'Catalog verification failed.' }
    $currenciesAfter = Invoke-PlayFab 'Admin/ListVirtualCurrencyTypes' @{}; Test-ApiSuccess $currenciesAfter 'ListVirtualCurrencyTypes after update'
    $verifiedCurrencies = @($currenciesAfter.data.VirtualCurrencies | Where-Object { $_.CurrencyCode -in $currencyCodes })
    if ($verifiedCurrencies.Count -ne 3 -or @($verifiedCurrencies | Where-Object { $_.InitialDeposit -ne 0 -or $_.RechargeRate -ne 0 -or $_.RechargeMax -ne 0 }).Count -ne 0) { Stop-Safe 'Virtual currency verification failed.' }
    $currencyMap=@{ CO='coins'; GM='gems'; EN='energy' }
    $privateSettings = [ordered]@{ Mode='PlayFabLegacyClosedQa'; CatalogVersion=$catalogVersion; PlayerWritePolicyVerified=$false; LegacyBindings=@($catalogItems | ForEach-Object { [ordered]@{ ItemId=$_.ItemId; ContentId=$_.ItemId } }); Currencies=@($currencyCodes | ForEach-Object { [ordered]@{ Code=$_; ContentId=$currencyMap[$_] } }) }
    $privateSettings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $privateRoot 'playfab-legacy-meta.json') -Encoding UTF8
    $manifest.MutationsApplied = $true; Write-SafeSummary $manifest
} catch { Write-Output ([ordered]@{Status='Stopped';Stage=$script:stage;ProviderErrorCode=$script:failureCode;ExceptionType=$_.Exception.GetType().Name}|ConvertTo-Json -Compress); throw 'Legacy QA provisioning stopped; credentials, account identities, and provider payloads were suppressed.' }
finally { $script:key=$null; $accounts=$null; if ($null -ne $script:http) { $script:http.Dispose() } }
