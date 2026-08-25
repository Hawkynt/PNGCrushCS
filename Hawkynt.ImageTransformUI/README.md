# Hawkynt.ImageTransformUI

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.ImageTransformUI.svg)](https://www.nuget.org/packages/Hawkynt.ImageTransformUI/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Targets](https://img.shields.io/badge/targets-net48%20%7C%20net8.0--windows%20%7C%20net10.0--windows-blue)

> Shared WinForms image-transformation UI for interactive color reduction, with live preview and registry-driven quantizer/ditherer selection.

## 📦 Installation

```bash
dotnet add package Hawkynt.ImageTransformUI
```

The package targets `net48`, `net8.0-windows`, and `net10.0-windows` and requires WinForms.

## ✨ Features

- `ReduceColorsWindow` modal color-reduction dialog.
- Registry-backed choice of 40+ quantizers and 120+ ditherers from `FrameworkExtensions.System.Drawing`.
- Palette-size control from 2 to 256 colors.
- Debounced asynchronous live preview with cancellation.
- `ForcePaletteSize(int)` for formats that require a fixed palette cardinality.
- Shared text-mode/font picker support through `FileFormat.TextMode`.

## 🧩 Capability support

| Capability | Support | Notes |
| --- | :---: | --- |
| WinForms UI | ✅ | Native Windows Forms dialog/window implementation. |
| Live preview | ✅ | Async/debounced rendering with cancellation. |
| Quantizer discovery | ✅ | Uses the FrameworkExtensions quantizer registry. |
| Ditherer discovery | ✅ | Uses the FrameworkExtensions ditherer registry. |
| Palette-size selection | ✅ | 2–256 colors. |
| Fixed palette size | ✅ | `ForcePaletteSize(int)` constrains formats such as monochrome targets. |
| Cross-platform UI | — | This is intentionally a WinForms/Windows package. |

Quantizer and ditherer names are not duplicated into a static documentation list because their registries are the authoritative source and can grow independently.

## 🚀 Quick start

```csharp
using Hawkynt.ImageTransformUI;

using var dialog = new ReduceColorsWindow(sourceBitmap);

// Optional for formats with a fixed palette size.
// dialog.ForcePaletteSize(2);

if (dialog.ShowDialog() == DialogResult.OK) {
  var quantizer = dialog.PickedQuantizerName;
  var ditherer = dialog.PickedDithererName;
  var paletteSize = dialog.PaletteSize;

  // Apply the selected transformation in the caller's image pipeline.
}
```

## 📚 API

| Member | Purpose |
| --- | --- |
| `ReduceColorsWindow(Bitmap)` | Create the color-reduction dialog for a source bitmap. |
| `ForcePaletteSize(int)` | Lock the palette-size choice to a format-required value. |
| `PickedQuantizerName` | Registry name selected by the user. |
| `PickedDithererName` | Registry name selected by the user. |
| `PaletteSize` | Selected palette cardinality. |

## 🏗️ Architecture

The package deliberately owns UI, not color science. Quantizers, ditherers, and their registry metadata come from `FrameworkExtensions.System.Drawing`; this package renders those choices, previews the result, and returns the selected configuration to the caller.

That keeps format projects and optimizers from each growing their own slightly different “reduce colors” dialog.

## 🔌 Dependencies

| Dependency | Role | Reference |
| --- | --- | --- |
| `FrameworkExtensions.System.Drawing` | Quantizer/ditherer registries and image-processing implementations. | [NuGet](https://www.nuget.org/packages/FrameworkExtensions.System.Drawing/) |
| `System.Drawing.Common` | Bitmap/GDI+ operations. | [Microsoft documentation](https://learn.microsoft.com/dotnet/api/system.drawing) |
| WinForms | Native Windows UI framework. | [Microsoft WinForms](https://learn.microsoft.com/dotnet/desktop/winforms/) |
| `FileFormat.TextMode` | Font/codepage preview infrastructure used by text-mode pickers. | Repository project |

## ⚠️ Limitations

- Windows-only by design because it is a WinForms package.
- Quantizer/ditherer availability follows the referenced FrameworkExtensions version; names should be treated as registry identifiers, not a frozen list owned by this package.
- The dialog returns the selected transformation parameters; the surrounding application remains responsible for file-format constraints and persistence.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../LICENSE).
