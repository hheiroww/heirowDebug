# heirowDebug.ProjectZ

![Project-Z framework logo](https://raw.githubusercontent.com/hheiroww/heirowDebug/main/Assets/project-z-framework.png)

Native Project-Z live debugging for .NET 10 Windows applications. This package
is maintained beside `heirowDebug.WPF` in the same repository. The inspector,
launcher/injector and protection tool use Project-Z scenes and native controls.
It depends on the publicly published `ProjectZ 2026.10.20.1` package.

```powershell
dotnet add package heirowDebug.ProjectZ --version 2026.10.20.2
```

```csharp
using heirowDebug.ProjectZ;

// Run this on your scene's owning thread during initialization.
var watcher = new DebugWatcher(gameState, Recursive: false);
watcher.AttachScene(scene);
watcher.StartSession();
DebugWatcher.CreateDebugWindow();

// When the application shuts down:
// watcher.Dispose();
```

`AttachScene` samples and applies edits in `Scene.FrameUpdated`, avoiding
cross-thread access to the native UI and renderer. Plain .NET objects can also
be sampled without a scene, using a captured synchronization context or a timer.
Keep scene sampling attached to a scene that continues updating while debugging.

The native inspector includes object and static watches, a searchable watch
picker, lazy collection/object/struct expansion, live field/property edits,
optional underscore backing-field writes, elapsed-time histories, gap compression,
a history range, and Auto, Time series, Spatial overlay and Events views. Numbers,
booleans, categories, colors, XNA geometry and ordinary .NET geometry have typed
visualizations. Sampling profiles match the WPF rates and retention limits.

Reflection is bounded to 24 watchers, six children per parent, two recursive
levels, 192 members per watcher and eight member reads per sampling pass by
default. A single member getter executes application code and cannot be safely
preempted; avoid watching getters that block. Automatic recursion is optional.

Build and launch the native tools:

```powershell
dotnet run --project heirowDebug.ProjectZ.Tools -c Release
```

The launcher finds accessible running Project-Z processes and restarts a selected
application with `heirowDebug.ProjectZ.Hook`. Save work before confirming a restart.
The startup hook discovers live application roots without PDBs, then creates the
debugger on the target scene thread. Static property getters run only when a watch
is added or sampled, never while browsing. Applications with private roots can
register them with `RootDiscovery.Register(scene)` and unregister at shutdown.
The hook supports .NET 10 hosts that allow startup hooks; protected/native hosts
and older managed runtimes are outside this hook's contract.

The protection page uses the same `ShieldService` and native pre-runtime launcher
as the WPF tool: copy a deployment, obfuscate private names/strings, remove PDBs,
disable startup hooks/managed diagnostics/hot reload, and verify an integrity
inventory. Choose an empty output folder outside the original deployment. The
original deployment is preserved. This protection does not prevent administrator
or arbitrary native memory access.

ProjectZ 2026.10.20.1 supplies a self-contained diagnostics observer carrying its
own runtime. The debugger ships that executable intact. A self-contained tools
publish includes the application's runtime as well; neither requires installing
the .NET 8 desktop runtime separately.

See `tools/Test-ProjectZ.ps1` for core and real native-window checks and the root
README for package publishing setup.
