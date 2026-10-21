# Project-Z framework artwork

The Project-Z framework symbol is a cyan-to-emerald beveled Z inside an open
geometric frame. It represents native graphics and layered user interfaces.
The symbol is distinct from the heirowDebug debugger waveform and cube artwork.

| File | Use |
| --- | --- |
| `project-z-framework-master.png` | Original lossless generated artwork with transparent alpha; preserve as the master |
| `project-z-framework.png` | 256 x 256 transparent PNG for NuGet and documentation |
| `project-z-framework.ico` | Windows icon containing 16, 24, 32, 48, 64, 128, and 256px PNG frames |
| `Export-ProjectZFrameworkBranding.ps1` | Rebuilds the package PNG and ICO from the master |

Created for Project-Z with OpenAI's image generation tool on October 20, 2026.
No third-party reference image or stock artwork was used. The master is a raster
image; an editable vector source is not available. The export script preserves
transparent alpha and changes only resolution and container format.

To regenerate the distribution assets on Windows:

```powershell
./Assets/Export-ProjectZFrameworkBranding.ps1
```

These assets are available for reuse by the Project-Z framework and its debugger.
The original heirowDebug artwork and existing application variants are preserved.
