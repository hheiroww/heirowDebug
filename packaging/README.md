# Windows installation

New standalone installers are built with `tools/Build-Installers.ps1`. Supply
the published heirowInstaller CLI and launcher, standalone heirowUpdater 2026.11.26.1
or later, the selected original Inno compiler, and the package-signing key from
outside the repository. Pass
`-LegalDocumentsDirectory` with one JSON file per product ID containing the
reviewed publisher Privacy Policy and Terms and Conditions. Each file uses
`privacyPolicy` and `termsAndConditions` objects with their URL and full content.
The script publishes all three apps with their .NET runtime, preserves each original
Inno upgrade identity, then creates signed standalone Project-Z setup EXEs. The
recipient does not need heirowInstaller installed. The optional **Application
updates** feature places `heirowUpdater.exe` and its generated profile at each
app's root. The app starts it and the companion checks after exit, offering
**Update now** or **Postpone**. The companion can reuse a supplied session for
protected downloads and keeps sign-in in the application or on the website.
Release publication keeps each setup and its SocketJack update feed in sync.

For a companion-only installer refresh, use
`scripts/Update-SignedApplicationCompanion.ps1` from the corresponding
heirowInstaller source release with `-Backend LegacyExecutable`. Use the
reviewed signed baseline for each product. This gives the signed installer
package a new version while preserving the exact shipped Inno backend,
application binaries, existing update profile and publisher identity.

The current [2026.10.23.5 release](https://github.com/hheiroww/heirowDebug/releases/tag/v2026.10.23.5)
includes self-contained Windows x64 portable ZIPs for the native Project-Z tools
and WPF injector, plus `heirowDebug.ProjectZ` and `heirowDebug.WPF` NuGet archives.
Extract a complete ZIP into its own folder before running the application.
Run `heirowDebug.ProjectZ.Tools.exe` for Project-Z or `heirowDebug.exe` for WPF.
The libraries are also available through NuGet.org and
[GitHub Packages](https://github.com/hheiroww?tab=packages&repo_name=heirowDebug).
`SHA256SUMS.txt` covers all downloadable release assets.

The installer instructions below apply to the previous 2026.10.17.1 release.

Download a Windows x64 installer or portable ZIP from
[GitHub Releases](https://github.com/hheiroww/heirowDebug/releases).
The applications include their .NET runtime. The native launcher is bundled
inside Shield, and the protected test application is a validation fixture rather
than a downloadable product.

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
