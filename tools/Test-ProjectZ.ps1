[CmdletBinding()]
param([switch]$Runtime, [switch]$Injector)
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
if ($Injector) {
    & dotnet build (Join-Path $repo 'heirowDebug.ProjectZ.Tools\heirowDebug.ProjectZ.Tools.csproj') -c Release -m:1 -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Project-Z injector build failed.' }
    $output = Join-Path $repo 'artifacts\injector-ui-result.json'
    $executable = Join-Path $repo 'heirowDebug.ProjectZ.Tools\bin\Release\net10.0-windows\heirowDebug.ProjectZ.Tools.exe'
    $check = Start-Process -FilePath $executable -ArgumentList @('--ui-check', ('"' + $output + '"')) -WorkingDirectory (Split-Path $executable -Parent) -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "Project-Z injector acceptance failed. See $output.error" }
    Get-Content -LiteralPath $output
}
