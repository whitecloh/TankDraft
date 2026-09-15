$ErrorActionPreference = 'Stop'
$verifyRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$verifySdk = Join-Path $verifyRoot 'Logs/BackendSdk/dotnet.exe'
$verifyRun = Join-Path $verifyRoot ('Logs/RemoteHost/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($verifyRun) | Out-Null
Push-Location (Join-Path $verifyRoot 'Backend')
try {
    & $verifySdk restore TankDraft.RemoteHost.Tests/TankDraft.RemoteHost.Tests.csproj --configfile NuGet.Config --locked-mode
    if ($LASTEXITCODE) { throw 'Remote host locked restore failed.' }
    & $verifySdk test TankDraft.RemoteHost.Tests/TankDraft.RemoteHost.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=remote-host.trx' --results-directory $verifyRun
    if ($LASTEXITCODE) { throw 'Remote host tests failed.' }
    $resultsFile = Join-Path $verifyRun 'remote-host.trx'
    if (!(Test-Path -LiteralPath $resultsFile)) { throw 'Remote host test results missing; no test run can be claimed.' }
    [xml]$results = [IO.File]::ReadAllText($resultsFile)
    $counters = $results.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.failed -ne 0 -or [int]$counters.executed -ne [int]$counters.total) { throw 'Remote host tests incomplete.' }
    Write-Output ('PASS remote host: ' + $counters.passed + ' tests; evidence ' + $verifyRun)
} finally { Pop-Location }
