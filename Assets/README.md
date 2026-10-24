# heirow brand artwork

The native Project-Z suite now uses `project-z-framework.png` and
`project-z-framework.ico`. Its original transparent master, reproducible export
script and provenance are documented in `project-z-framework-branding.md`.
The previous cube variant remains preserved below.

Created with an image generation tool. The original brand artwork is preserved as
`heirowDebug.png` with its matching multi-resolution `heirowDebug.ico`.

The debugger retains `heirowDebug.WPF` as its library identity and CLR namespace
for existing hooks and callers.

## 2026 application variants

Generated with the built-in `image_gen.imagegen` tool, using the original
`heirowDebug.png` as the reference and transparent backgrounds. The reference
artwork is preserved. The variants are resized to 256px PNGs and multi-resolution
Windows ICO files (16, 24, 32, 48, 64, 128, and 256px). EXEs, installer windows,
and shortcuts all use the corresponding ICO.

| Application | Artwork | Role badge |
| --- | --- | --- |
| heirowDebug injector | `heirowDebug-Injector.png` / `.ico` | Arrow entering an application window |
| heirowDotNetShield, including its native launcher | `heirowDotNetShield.png` / `.ico` | Shield and keyhole |
| Project-Z sample | `heirowDebug-ProjectZ.png` / `.ico` | Three-dimensional cube |

`heirowDebug-logo.png` is the original brand icon's 256px PNG entry, extracted
without re-encoding for the lightweight README header. `heirowDebug-live-demo.gif`
records the real .NET 10 debugger watching changing sample telemetry; it is not
generated artwork.

## Native Project-Z recording

`heirowDebug-ProjectZ-demo.gif` records the production native inspector using
synthetic telemetry, with 16x anti-aliasing and 16x anisotropic filtering.
It contains 96 frames at 10 fps, displayed at 1024 by 640 pixels. The recording
shows labeled numeric/time axes, current values, accessor hooks, dragging both
history-range endpoints, a frozen graph while background telemetry continues,
a live spatial trace and boolean state bands.

Capture fresh frames into an empty output directory:

```powershell
dotnet run --project tools/ProjectZLiveDemo/ProjectZLiveDemo.csproj -c Release -- artifacts/native-demo-frames
```

The harness writes PNG frames and a capture receipt under `artifacts`.
The checked-in GIF uses Lanczos scaling, a 128-color palette, no dithering and
rectangle-based frame differences. Desktop content and live account/application
data are excluded from the recording.
