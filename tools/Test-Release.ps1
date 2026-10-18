[CmdletBinding()]
param([switch]$Registered)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$release = Join-Path $repo "artifacts\release-$version"
$payload = Join-Path $repo "artifacts\publish-$version"
$testRoot = Join-Path $repo ("artifacts\verify-$version-" + (Get-Date -Format yyyyMMdd-HHmmss))
if (Test-Path -LiteralPath $testRoot) { throw "Test directory already exists: $testRoot" }
New-Item -ItemType Directory $testRoot | Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$shell = New-Object -ComObject WScript.Shell
$installed = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$ownedProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
$products = @(
    @{ Folder='heirowDebug'; Name='heirowDebug 2026'; Id='heirow.heirowDebug'; Exe='heirowDebug.exe' },
    @{ Folder='heirowDotNetShield'; Name='heirowDotNetShield 2026'; Id='heirow.heirowDotNetShield'; Exe='heirowDotNetShield.exe' },
    @{ Folder='heirowDebug-ProjectZ'; Name='heirowDebug Project-Z Sample'; Id='heirow.heirowDebug.ProjectZ'; Exe='heirowDebug.ProjectZ.exe' }
)
function Invoke-CheckedProcess([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Timed out waiting for PID $($process.Id): $File" }
    if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $File" }
}
function Wait-VisibleWindow($Process) {
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500
        $Process.Refresh()
        if ($Process.HasExited) { throw "App exited before displaying a window: $($Process.ExitCode)" }
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and $Process.Responding) { return }
    }
    throw "No responsive window for PID $($Process.Id)"
}
function Close-TestWindows($Process) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) {
        $pattern = $null
        if ($window.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$pattern)) {
            ([Windows.Automation.WindowPattern]$pattern).Close()
        }
    }
    if (-not $Process.WaitForExit(10000)) { throw "Test app did not close normally: PID $($Process.Id)" }
    if ($Process.ExitCode -ne 0) { throw "Test app closed with exit code $($Process.ExitCode)" }
}
try {
    foreach ($product in $products) {
        $target = Join-Path $testRoot $product.Folder
        $registry = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($product.Id)_is1"
        $startLink = Join-Path ([Environment]::GetFolderPath('Programs')) "heirow\$($product.Name).lnk"
        $desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$($product.Name).lnk"
        if ($Registered -and ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $startLink) -or (Test-Path -LiteralPath $desktopLink))) {
            throw "Existing installation or shortcut for $($product.Name); use portable validation to preserve it."
        }
        $setup = Join-Path $release "$($product.Folder)-$version-windows-x64-Setup.exe"
        $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-',"/DIR=`"$target`"", "/LOG=`"$target-setup.log`"")
        if ($Registered) { $arguments += '/TASKS=desktopicon' } else { $arguments += '/PORTABLE=1' }
        Invoke-CheckedProcess $setup $arguments
        $installed.Add(@{ Target=$target; Registry=$registry; StartLink=$startLink; DesktopLink=$desktopLink })
        $source = Join-Path $payload $product.Folder
        $verified = 0
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
            $actual = Join-Path $target $relative
            if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $actual).Hash) { throw "Payload mismatch: $actual" }
            $verified++
        }
        if ($Registered) {
            if (-not (Test-Path -LiteralPath $registry)) { throw 'Uninstall registration missing' }
            foreach ($linkPath in @($startLink,$desktopLink)) {
                $link = $shell.CreateShortcut($linkPath)
                $expected = Join-Path $target $product.Exe
                if ($link.TargetPath -ne $expected -or -not $link.IconLocation.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw "Shortcut target or branding mismatch: $linkPath" }
            }
            if ((Get-ItemProperty -LiteralPath $registry).DisplayIcon -notlike "*$($product.Exe)*") { throw 'Uninstall icon mismatch' }
        } elseif (Test-Path -LiteralPath (Join-Path $target 'unins000.exe')) { throw 'Portable mode created an uninstaller' }
        $app = Start-Process -FilePath (Join-Path $target $product.Exe) -PassThru
        $ownedProcesses.Add($app)
        Wait-VisibleWindow $app
        Start-Sleep -Seconds 2
        $app.Refresh()
        if (-not $app.Responding) { throw "Unresponsive app: $($product.Name)" }
        $results.Add(@{ app=$product.Name; filesVerified=$verified; title=$app.MainWindowTitle; responsive=$app.Responding; registered=[bool]$Registered; shortcutIconsVerified=[bool]$Registered })
        Close-TestWindows $app
    }

    # Exercise the installed startup hook against a real .NET 10 WPF process.
    $debugger = Join-Path $testRoot 'heirowDebug'
    $fixture = Join-Path $repo 'heirowDebug.ProtectedTestApp\bin\Release\net10.0-windows\heirowDebug.ProtectedTestApp.exe'
    $status = Join-Path $testRoot 'injection.status'
    $start = [Diagnostics.ProcessStartInfo]::new($fixture)
    $start.UseShellExecute = $false
    $start.Environment['DOTNET_STARTUP_HOOKS'] = Join-Path $debugger 'heirowDebug.Injector.Hook.dll'
    $start.Environment['heirowDebug_PAYLOAD_DIRECTORY'] = $debugger
    $start.Environment['heirowDebug_STATUS_FILE'] = $status
    $start.Environment['heirowDebug_RECURSIVE'] = '0'
    $injected = [Diagnostics.Process]::Start($start)
    $ownedProcesses.Add($injected)
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        if (Test-Path -LiteralPath $status) {
            $state = Get-Content -LiteralPath $status -Raw
            if ($state.StartsWith('READY|')) { $ready=$true; break }
            if ($state.StartsWith('ERROR|')) { throw $state }
        }
        if ($injected.HasExited) { throw 'Injected fixture exited early' }
    }
    if (-not $ready) { throw 'Installed startup hook did not report READY' }
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$injected.Id)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)
    $titles = @($windows | ForEach-Object { $_.Current.Name })
    if (-not ($titles | Where-Object { $_ -like "heirowDebug - * (PID $($injected.Id))" })) { throw 'The actual injected debugger window is missing' }
    $results.Add(@{ injectionReady=$state.Trim(); windowTitles=$titles })
    Close-TestWindows $injected

    # Protect a disposable build using the installed Shield, then execute it.
    $protected = Join-Path $testRoot 'protected-fixture'
    $shield = Join-Path $testRoot 'heirowDotNetShield\heirowDotNetShield.exe'
    Invoke-CheckedProcess $shield @('--protect', "`"$([IO.Path]::ChangeExtension($fixture,'.dll'))`"", "`"$protected`"")
    $report = Join-Path $testRoot 'protected-self-test.json'
    $protectedExe = Join-Path $protected 'heirowDebug.ProtectedTestApp.exe'
    Invoke-CheckedProcess $protectedExe @('--self-test',"`"$report`"")
    $selfTest = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $selfTest.passed) { throw 'Protected application self-test failed' }
    $results.Add(@{ protectedSelfTest=$selfTest })
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testRoot 'results.json')
    $results | ConvertTo-Json -Depth 6
} finally {
    foreach ($ownedProcess in $ownedProcesses) {
        if (-not $ownedProcess.HasExited) { Close-TestWindows $ownedProcess }
    }
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testRoot 'results.json')
    if ($Registered) {
        foreach ($installation in $installed) {
            $uninstaller = Join-Path $installation.Target 'unins000.exe'
            if (Test-Path -LiteralPath $uninstaller) {
                Invoke-CheckedProcess $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
                foreach ($ownedPath in @($installation.Registry,$installation.StartLink,$installation.DesktopLink)) {
                    if (Test-Path -LiteralPath $ownedPath) { throw "Uninstall left a registered item: $ownedPath" }
                }
            }
        }
    }
}
