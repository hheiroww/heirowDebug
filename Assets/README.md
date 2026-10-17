# heirow brand artwork

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
