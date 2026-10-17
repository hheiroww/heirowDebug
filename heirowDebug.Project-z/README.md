# heirowDebug Project-Z sample

This Windows x64 starter application creates a Project-Z scene using the published `ProjectZ` NuGet package. It is included in `heirowDebug.WPF.sln` and uses the MonoGame native DirectX 12 backend supplied by Project-Z. It is a starter scene, not a completed port of the WPF debugger.

## Build and run

The [2026.10.17.1 release](https://github.com/hheiroww/heirowDebug/releases/tag/v2026.10.17.1)
includes a branded Windows x64 Setup EXE and portable ZIP for this sample. Both
include .NET 10 and the Project-Z dependencies, so the SDK is only needed to build
from source.

Install the .NET 10 SDK or newer on Windows, then run from the repository root:

```powershell
dotnet restore .\heirowDebug.Project-z\heirowDebug.Project-z.vbproj
dotnet run --project .\heirowDebug.Project-z\heirowDebug.Project-z.vbproj -c Release
```

The project pins `ProjectZ` version `2026.10.17.1`, which bundles its internal model-import DLL without requesting an unpublished package. No sibling framework checkout or local package feed is required. The old KNI and Triangle references are replaced by the dependencies shipped with Project-Z.

`GameWindow.vb` configures the window and scene manager. `Scenes/MainScene.vb` defines the initial scene. The project has a concrete namespace, assembly name, and startup object so it builds directly without Visual Studio template substitution.
