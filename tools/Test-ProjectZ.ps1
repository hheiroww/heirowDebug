[CmdletBinding()]
param([switch]$Runtime, [switch]$Injector, [switch]$Hooks, [string]$TargetProjectZVersion)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Path (Join-Path $repo 'artifacts') -Force | Out-Null
& dotnet run --project (Join-Path $repo 'tests\ProjectZ\ProjectZ.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Project-Z core acceptance failed.' }
if ($Runtime) {
    $output = Join-Path $repo 'artifacts\projectz-runtime-result.txt'
    & dotnet run --project (Join-Path $repo 'tests\ProjectZ\ProjectZ.Tests.csproj') -c Release --no-build -- --runtime $output
    if ($LASTEXITCODE -ne 0) { throw 'Project-Z native-window acceptance failed.' }
    Get-Content -LiteralPath $output
}
if ($Injector -or $Hooks) {
    & dotnet build (Join-Path $repo 'heirowDebug.ProjectZ.Tools\heirowDebug.ProjectZ.Tools.csproj') -c Release -m:1 -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Project-Z injector build failed.' }
}
if ($Injector) {
    $output = Join-Path $repo 'artifacts\injector-ui-result.json'
    $executable = Join-Path $repo 'heirowDebug.ProjectZ.Tools\bin\Release\net10.0-windows\heirowDebug.ProjectZ.Tools.exe'
    $check = Start-Process -FilePath $executable -ArgumentList @('--ui-check', ('"' + $output + '"')) -WorkingDirectory (Split-Path $executable -Parent) -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "Project-Z injector acceptance failed. See $output.error" }
    Get-Content -LiteralPath $output
}
if ($Hooks) {
    $targetArguments = @('build', (Join-Path $repo 'tests\ProjectZ.Target\ProjectZ.Target.csproj'), '-c', 'Release', '-m:1')
    if ($TargetProjectZVersion) { $targetArguments += ('-p:ProjectZVersion=' + $TargetProjectZVersion) }
    & dotnet @targetArguments
    if ($LASTEXITCODE -ne 0) { throw 'The unreferenced injection target did not build.' }
    $targetDirectory = Join-Path $repo 'tests\ProjectZ.Target\bin\Release\net10.0-windows'
    foreach ($dependency in @('heirowDebug.ProjectZ.dll', '0Harmony.dll')) {
        if (Test-Path -LiteralPath (Join-Path $targetDirectory $dependency)) { throw 'The target unexpectedly contains a debugger dependency.' }
    }
    $payload = Join-Path $repo 'heirowDebug.ProjectZ.Tools\bin\Release\net10.0-windows'
    $output = Join-Path $repo ('artifacts\accessor-injection-' + [Guid]::NewGuid().ToString('N') + '.json')
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $targetDirectory 'NativeTelemetryTarget.exe'))
    $start.UseShellExecute = $false; $start.WorkingDirectory = $targetDirectory; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.ArgumentList.Add('--observe-result'); $start.ArgumentList.Add($output)
    $start.Environment['DOTNET_STARTUP_HOOKS'] = Join-Path $payload 'heirowDebug.ProjectZ.Hook.dll'
    $start.Environment['heirowDebug_PROJECTZ_PAYLOAD'] = $payload
    $start.Environment['heirowDebug_STATUS_FILE'] = $output + '.status'
    $start.Environment['heirowDebug_ROOT_TYPE'] = 'TargetGame'
    $target = [Diagnostics.Process]::Start($start)
    try {
        if (-not $target.WaitForExit(45000)) { throw 'The injected property observation probe timed out.' }
        if (Test-Path -LiteralPath ($output + '.error')) { throw ('The injected observation probe failed. See ' + $output + '.error') }
        if ($target.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw 'The injected observation target did not report success.' }
        $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if (-not $result.harmonyLoaded -or $result.capturedTransitions -lt 20 -or $result.spellCheckEnabled) { throw 'The injected observation receipt failed validation.' }
        Get-Content -LiteralPath $output
    }
    finally { if (-not $target.HasExited) { [void]$target.CloseMainWindow(); if (-not $target.WaitForExit(3000)) { $target.Kill() } }; $target.Dispose() }
}
