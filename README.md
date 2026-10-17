# heirowDebug 2026

**Version 2026.10.17.0** · Windows · .NET 8

heirowDebug is a live object value visualizer for WPF applications. Launch `heirowDebug.exe` to select an application to debug, or reference `heirowDebug.WPF` in your own application to watch objects directly. This is the 2026 release of the former Jack-Debug project.

## Download and build

Get the Windows launcher and library package from [GitHub Releases](https://github.com/hheiroww/heirowDebug/releases). Extract the launcher archive before running `heirowDebug.exe`; keep its supporting DLLs beside it. The launcher requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) on Windows.

To build all projects, install the .NET 8 SDK or newer and Visual Studio's **Desktop development with C++** workload (required by the native heirowDotNetShield launcher), then run:

```powershell
dotnet build .\heirowDebug.WPF.sln -c Release
.\heirowDebug.Injector\bin\Release\net8.0-windows\heirowDebug.exe
```

The solution also includes a [Project-Z sample](https://github.com/hheiroww/heirowDebug/blob/main/heirowDebug.Project-z/README.md), the heirowDotNetShield protection tool, and a disposable WPF validation application.

In Visual Studio, open `heirowDebug.WPF.sln` and set **heirowDebug.Injector** as the startup project if an existing user setting selects the WPF library. Use **Debug** or **Release** with **Any CPU**; the Project-Z sample and native protection launcher select x64 internally.


## Setup
Download `heirowDebug.WPF.2026.10.17.nupkg` from the release and place it in a local package folder such as `C:\Packages`. Register that folder once, then add the library to your application:

```powershell
dotnet nuget add source C:\Packages --name heirowDebug-releases
dotnet add package heirowDebug.WPF --version 2026.10.17.0
```

The renamed `heirowDebug.WPF` package is distributed with this GitHub release; it has not been published to nuget.org. Existing callers of `JackDebug.WPF` must update their package reference and imports to `heirowDebug.WPF`.

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
![Demo](https://raw.githubusercontent.com/hheiroww/heirowDebug/refs/heads/main/demo1100.gif)

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
.\heirowDebug.Injector\bin\Release\net8.0-windows\heirowDebug.exe
```

The restart is required because this version uses .NET's supported launch-time
hook rather than a native CLR profiler. Save work in the selected process first.
The target executable and its source do not need to reference heirowDebug, and the
injector does not copy files into the target application's directory.
The target must be an accessible managed .NET WPF process that honors .NET
startup hooks. Native applications, WinForms-only applications, elevated
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
.\heirowDotNetShield\bin\Release\net8.0-windows\heirowDotNetShield.exe
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
