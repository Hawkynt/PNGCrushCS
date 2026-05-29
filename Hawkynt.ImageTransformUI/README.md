# Hawkynt.ImageTransformUI

A WinForms shared library providing an interactive **color reduction dialog** with live preview, quantizer/ditherer selection, and palette size control.

## Features

- **ReduceColorsWindow** — modal dialog for color quantization
  - Full quantizer registry (40+ algorithms: Octree, Median Cut, Wu, K-Means, Neural, etc.)
  - Full ditherer registry (120+ algorithms: Floyd-Steinberg, Bayer, Blue Noise, etc.)
  - Palette size slider (2–256) with debounced live preview
  - `ForcePaletteSize(int)` for formats requiring specific palette depths (e.g., monochrome = 2)
  - Async preview rendering with cancellation support

## Usage

```csharp
using Hawkynt.ImageTransformUI;

using var dialog = new ReduceColorsWindow(sourceBitmap);
// Optional: force palette for monochrome formats
// dialog.ForcePaletteSize(2);

if (dialog.ShowDialog() == DialogResult.OK) {
    string quantizer = dialog.PickedQuantizerName;   // e.g., "Median Cut"
    string ditherer = dialog.PickedDithererName;      // e.g., "ErrorDiffusion_FloydSteinberg"
    int paletteSize = dialog.PaletteSize;             // e.g., 256
}
```

## Dependencies

- [FrameworkExtensions.System.Drawing](https://www.nuget.org/packages/FrameworkExtensions.System.Drawing) — quantizer/ditherer registries (Roslyn source-generator based)
- System.Drawing.Common — GDI+ bitmap operations
- WinForms (net10.0-windows)

## License

LGPL-3.0-or-later
