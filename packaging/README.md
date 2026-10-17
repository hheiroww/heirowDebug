# Windows releases

Run from the repository root with .NET 10 SDK, Visual Studio's .NET desktop and
C++ workloads, and Inno Setup 7's compiler:

```powershell
./tools/Build-Release.ps1 -IsccPath 'C:\Path\To\Inno Setup 7\ISCC.exe'
```

The version comes from `Directory.Build.props`. Each application is published
self-contained for Windows x64, without trimming. Outputs go to
`artifacts/release-<version>` and publish payloads to `artifacts/publish-<version>`.
Existing payload directories are rejected so stale files cannot enter a release.
Use a fresh version or archive the exact previous payload directory before
rebuilding. The native launcher is bundled inside Shield, and the protected test
application is a validation fixture rather than a downloadable product.

Normal installers register per-user uninstall entries and create Start menu
shortcuts under **heirow**. The optional desktop shortcuts, installer windows,
uninstall entries, and app EXEs use each product's icon. Installers do not stop
running applications automatically. Close a running copy before upgrading it.

For isolated validation or portable extraction, each release installer accepts:

```powershell
./heirowDebug-2026.10.17.1-windows-x64-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /SP- /PORTABLE=1 /DIR=C:\Temp\heirowDebug-smoke
```

Portable mode writes only the payload directory: no shortcuts, uninstall
registration, or uninstaller. Always choose an empty test directory. Validate
the extracted application's real UI as well as byte-for-byte payload hashes.
For the registered installation path, verify Start menu and optional desktop
shortcuts resolve to the installed EXE and its embedded product icon; uninstall
the test installation normally afterward.

`./tools/Test-Release.ps1` automates portable extraction, payload hash comparison,
real UI launch/close for each application, startup-hook injection into the .NET 10
fixture, and a protected-app self-test using the extracted Shield. Build the
solution in Release first to produce that fixture. Add `-Registered` to test real
per-user installation, Start menu and desktop shortcut icons, uninstall
registration, and normal uninstallation. Registered validation refuses to run
over any existing product installation or shortcut.

`SHA256SUMS.txt` covers the three EXE installers, three portable ZIPs, two raw
library DLLs, and NuGet package. Third-party assemblies remain beside the apps;
their license information is available from the corresponding NuGet packages.

## Live README recording

```powershell
dotnet run --project tools/LiveDemo/LiveDemo.csproj -c Release -- artifacts/demo-frames
```

The harness launches the production debugger and mutates sample telemetry on
its WPF dispatcher. It selects real numeric, point, and boolean members and
captures 110 rendered frames. `capture.json` records the runtime, selected
members, and observed value range. The README GIF uses frames 0–39, 55–74, and
85–104, encoded at 6fps and 760px with FFmpeg's
`palettegen=max_colors=96:stats_mode=full` and
`paletteuse=dither=none:diff_mode=rectangle` filters. It runs for eight seconds
and is 556 KB. The GIF is a live application recording.
