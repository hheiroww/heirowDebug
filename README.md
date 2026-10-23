![heirowDebug logo](https://raw.githubusercontent.com/hheiroww/heirowDebug/main/Assets/heirowDebug-logo.png)

# heirowDebug 2026

**Live objects. Clear timelines. Visual debugging.**

Version 2026.10.23.1 · Windows · .NET 10

heirowDebug provides live object inspection, editing and typed timelines for WPF and native Project-Z applications. Both implementations live in this repository and are published as separate NuGet packages: **heirowDebug.WPF** and **heirowDebug.ProjectZ**. This is the 2026 release of the former Jack-Debug project.

## Current packages and Project-Z tools

```powershell
dotnet add package heirowDebug.WPF --version 2026.10.23.1
# For a native Project-Z application:
dotnet add package heirowDebug.ProjectZ --version 2026.10.23.1
```

The Project-Z suite includes the native live inspector, a running-process
launcher/startup hook, and a native protection page using the same protection
engine and pre-runtime launcher as the WPF tool. It also includes a runnable VB
scene sample. See [Project-Z integration and behavior](heirowDebug.ProjectZ/README.md).

The native injector uses the same XAML layout as the WPF injector, with a directly
selectable process table, keyboard navigation, scrolling, and 16x AA/AF.

```powershell
dotnet run --project heirowDebug.ProjectZ.Tools -c Release
```

Sampling and edits run on the owning scene thread when the watcher is attached
with `AttachScene(scene)`. Reflection, history retention and recursive expansion
are bounded. XNA points, vectors, rectangles and colors have native visualizations.

Get the libraries from NuGet: [heirowDebug.WPF](https://www.nuget.org/packages/heirowDebug.WPF/)
and [heirowDebug.ProjectZ](https://www.nuget.org/packages/heirowDebug.ProjectZ/).

The installer links below are the previous **2026.10.17.1** standalone release.

## Download and build

Get version **2026.10.17.1** from [GitHub Releases](https://github.com/hheiroww/heirowDebug/releases/tag/v2026.10.17.1). Windows x64 installers include .NET 10 and create branded Start menu shortcuts; desktop shortcuts are optional. Installation is per user and does not require administrator access.

| Software | Installer | Portable archive |
| --- | --- | --- |
| heirowDebug live debugger / injector | [Download Setup EXE](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug-2026.10.17.1-windows-x64-Setup.exe) | [ZIP](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug-2026.10.17.1-windows-x64.zip) |
| heirowDotNetShield protection tool | [Download Setup EXE](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDotNetShield-2026.10.17.1-windows-x64-Setup.exe) | [ZIP](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDotNetShield-2026.10.17.1-windows-x64.zip) |
| Project-Z starter sample | [Download Setup EXE](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug-ProjectZ-2026.10.17.1-windows-x64-Setup.exe) | [ZIP](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug-ProjectZ-2026.10.17.1-windows-x64.zip) |

Extract an entire portable ZIP before running its EXE. The native Shield launcher and all supporting files are included with Shield. The Project-Z download runs the included starter scene.

For developers: download [heirowDebug.WPF.dll](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug.WPF.dll), [heirowDebug.Injector.Hook.dll](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug.Injector.Hook.dll), or the [NuGet package](https://github.com/hheiroww/heirowDebug/releases/download/v2026.10.17.1/heirowDebug.WPF.2026.10.17.1.nupkg). The raw DLLs require a .NET 10 WPF host and their dependencies; using the package restores the WPF library dependencies automatically. The hook also needs the debugger DLL and the supporting DLLs from the debugger ZIP. `SHA256SUMS.txt` accompanies every release asset.

To build all projects, install the .NET 10 SDK and Visual Studio 2026 with the **.NET desktop development** and **Desktop development with C++** workloads (the latter is required by the native heirowDotNetShield launcher), then run:

```powershell
dotnet build .\heirowDebug.WPF.sln -c Release
.\heirowDebug.Injector\bin\Release\net10.0-windows\heirowDebug.exe
```

The solution also includes a [Project-Z sample](https://github.com/hheiroww/heirowDebug/blob/main/heirowDebug.Project-z/README.md), the heirowDotNetShield protection tool, and a disposable WPF validation application.

In Visual Studio, open `heirowDebug.WPF.sln` and set **heirowDebug.Injector** as the startup project if an existing user setting selects the WPF library. Use **Debug** or **Release** with **Any CPU**; the Project-Z sample and native protection launcher select x64 internally.

Brand artwork and application icon variants are documented in [Assets/README.md](https://github.com/hheiroww/heirowDebug/blob/main/Assets/README.md).

See [Windows installation options](packaging/README.md) for installer and portable usage.

## Setup
Add the library to your WPF application from NuGet:

```powershell
dotnet add package heirowDebug.WPF --version 2026.10.23.1
```

The older `2026.10.17.1` package is distributed with that GitHub release. Current package instructions are listed above. Existing callers of `JackDebug.WPF` must update their package reference and imports to `heirowDebug.WPF`.

### C#
```cs
    using heirowDebug.WPF;

    var dw = new DebugWatcher(Game, true);
    dw.StartSession();

    // Optional global sampling profile. Fast (60 checks/second) is the default.
    Utils.Profile = Utils.DebugProfile.NearInstant;
```

### VB.NET
```vb
    Imports heirowDebug.WPF

    Dim dw As New DebugWatcher(Game, True)
    dw.StartSession()

    ' Optional: NearInstant, Fast, Medium, Slow, or PowerSaving.
    Profile = DebugProfile.PowerSaving
```

## Sampling profiles

`Profile` tunes both polling frequency and in-memory snapshot retention for all
active watchers. Changes apply live: `NearInstant` uses 120 Hz, `Fast` 60 Hz,
`Medium` 30 Hz, `Slow` 10 Hz, and `PowerSaving` checks once per second. You can
still set `FramesPerSecond` directly when a custom rate is needed.


## Show the debug window.
### C#
```cs
    DebugWatcher.CreateDebugWindow();
```

### VB.NET
```vb
    DebugWatcher.CreateDebugWindow()
```

## Demo

### Native Project-Z

![Native Project-Z debugger: live timelines, spatial traces and boolean states](https://raw.githubusercontent.com/hheiroww/heirowDebug/main/Assets/heirowDebug-ProjectZ-demo.gif)

A 9.6-second recording of the native inspector watching sample telemetry, with
16x AA/AF, a live numeric timeline, a spatial trace and boolean state bands.

### WPF

![Live .NET 10 debugger: numeric timelines, point trails, and boolean states](https://raw.githubusercontent.com/hheiroww/heirowDebug/main/Assets/heirowDebug-live-demo.gif)

An 8-second recording of the actual .NET 10 debugger watching changing telemetry, captured with [tools/LiveDemo](tools/LiveDemo). The optimized 760px GIF is **556 KB**, about 82% smaller than the previous demo; no video player is required.

## Inject into an existing WPF application build

`heirowDebug.exe` presents the accessible running WPF processes in a
selector. Pick an application and use **Restart + inject**. The injector restarts
that executable with a managed startup hook; once WPF creates its first window,
the hook adds every instantiated application window as a root watcher and opens
the live heirowDebug window on the application's WPF dispatcher. Windows created
later are discovered by the watch picker and can be added without reinjection.

The preferred-startup-window override is optional. It only controls which window
is listed first; it no longer limits the debug session to that one type. In the
injected debugger, use **+ Watch...** beside the Debugging switch to search live
windows, loaded application types with static members, and reference-type
objects held by static fields or properties. Static property getters are not
executed while browsing: the selected member is resolved only when **Add
selected** is pressed, and a null member is reported rather than watched. This
does not require PDB files or other debug information.

```powershell
dotnet build .\heirowDebug.WPF.sln -c Release
.\heirowDebug.Injector\bin\Release\net10.0-windows\heirowDebug.exe
```

The restart is required because this version uses .NET's supported launch-time
hook rather than a native CLR profiler. Save work in the selected process first.
The target executable and its source do not need to reference heirowDebug, and the
injector does not copy files into the target application's directory.
The target must be an accessible managed **.NET 10 WPF** process that honors .NET
startup hooks. This version's .NET 10 hook cannot load into older runtimes; use
the [2026.10.17.0 release](https://github.com/hheiroww/heirowDebug/releases/tag/v2026.10.17.0)
for .NET 8 hosts. Native applications, WinForms-only applications, elevated
processes inaccessible to the injector, and legacy .NET Framework hosts are not
made injectable by debug symbols alone.

### Injected-process performance safety

Injection defaults to 15 sampling passes per second with automatic recursive
object expansion disabled. Sampling rotates through at most eight members per
pass instead of rereading an entire large window continuously. Runtime guards
limit a session to 24 watchers, six children per parent, two recursive levels,
and 192 reflected members per watcher; ancestor identity checks stop cyclic
object graphs from walking back into themselves. Compound reference values are
inspected lazily when their tree node is expanded. These bounds can be adjusted
through `DebugWatcher` and `Utils` when a smaller application needs a more
aggressive profile.

## Live visualizations

Select any value in the watcher tree to graph its recorded snapshots. **Auto**
chooses a visualization that fits the runtime type: line and area charts for
numbers, state bands for booleans, category bands for strings and enums, color
strips for colors, four-channel plots for `Thickness`, and shape trails for
`Point`, `Vector`, `Size`, and `Rect` values. Rectangle histories preserve each
sample's position and dimensions, with color indicating time. Use **Spatial
overlay** to plot geometry in its own coordinate system or **Events** to inspect
individual changes. Time spacing is accurate by default, so long pauses remain
visible with the prior value held across the gap. Enable **Compact gaps** and
adjust its slider to blend back toward evenly spaced changes. The range slider
limits the visible history.

Drag the divider beside the watcher tree to resize it. Compound values can be
expanded to inspect a bounded list of their fields and readable properties,
including useful framework structs whose default `ToString()` only reports a
type name.

## Protect a compiled .NET application

`heirowDotNetShield` creates a new protected deployment from the main application
DLL of a Release build. It never rewrites the selected source deployment. The
protected copy receives private-name and string-literal obfuscation, no PDBs,
runtime feature switches that reject startup hooks/debugger support, and a native
pre-runtime launcher that clears startup-hook requests and disables the .NET
diagnostic IPC channel before CoreCLR is loaded.

```powershell
dotnet build .\heirowDotNetShield\heirowDotNetShield.csproj -c Release
.\heirowDotNetShield\bin\Release\net10.0-windows\heirowDotNetShield.exe
```

Select the application's main `.dll`, choose an empty output directory outside
the original deployment directory, and create the protected build. The output
contains `heirowDotNetShield-report.json` and `heirowDotNetShield.sha256`. Sign the
finished output after protection; rewriting invalidates an existing Authenticode
signature. Strong-name signed inputs are rejected until a signing-key workflow is
configured.

The disposable `heirowDebug.ProtectedTestApp` project is the validation target. In
the verified protected build, reflection initiated by the application itself
still works, while heirowDebug's `DOTNET_STARTUP_HOOKS` payload does not load and
the standard `dotnet-diagnostic-{pid}` pipe is not created. These controls do not
form an absolute boundary against an administrator, a modified native loader, or
direct memory access on the same computer. Modern .NET does not provide a secure
"allow reflection only when the caller is this DLL" permission once foreign code
is already executing in the process.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

## License

[MIT](https://choosealicense.com/licenses/mit/)
