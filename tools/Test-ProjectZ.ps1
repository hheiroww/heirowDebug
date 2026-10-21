[CmdletBinding()]
param([switch]$Runtime)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& dotnet run --project (Join-Path $repo 'tests\ProjectZ\ProjectZ.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Project-Z core acceptance failed.' }
if ($Runtime) {
    $output = Join-Path $repo 'artifacts\projectz-runtime-result.txt'
    & dotnet run --project (Join-Path $repo 'tests\ProjectZ\ProjectZ.Tests.csproj') -c Release --no-build -- --runtime $output
    if ($LASTEXITCODE -ne 0) { throw 'Project-Z native-window acceptance failed.' }
    Get-Content -LiteralPath $output
}
