# Project-Z debugger sample

This native scene demonstrates `heirowDebug.ProjectZ`. Run it and choose
**Open live debugger** to inspect changing numeric, boolean, color, list and XNA
geometry values. Sampling and editing use the scene's owning frame thread.

```powershell
dotnet run --project heirowDebug.Project-z/heirowDebug.Project-z.vbproj -c Release
```

Its executable is `heirowDebug.ProjectZ.Sample.exe`, keeping the package assembly
identity `heirowDebug.ProjectZ.dll` available to consumers. The complete native
inspector, launcher/injector and protection suite is in `heirowDebug.ProjectZ.Tools`.
The main README documents the separate WPF and Project-Z packages.