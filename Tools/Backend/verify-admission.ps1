param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnet = Join-Path $root 'Logs/BackendSdk/dotnet.exe'
$project = Join-Path $root 'Backend/TankDraft.AdmissionChecks/TankDraft.AdmissionChecks.csproj'
if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'Local .NET SDK is missing under Logs/BackendSdk.' }
if (!(Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Admission checks project is missing.' }
Push-Location (Join-Path $root 'Backend')
try {
    & $dotnet restore $project --configfile NuGet.Config --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked admission restore failed.' }
    & $dotnet run --project $project --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Admission offline checks failed.' }
}
finally { Pop-Location }
