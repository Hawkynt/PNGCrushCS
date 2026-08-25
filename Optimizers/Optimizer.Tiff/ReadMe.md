# Optimizer.Tiff

[![NuGet](https://img.shields.io/nuget/v/Optimizer.Tiff.svg)](https://www.nuget.org/packages/Optimizer.Tiff/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-blue)

> TIFF optimization engine that searches compression, predictor, color-mode, strip-size, and optional tile layouts and returns the smallest valid TIFF candidate.

## 📦 Installation

```bash
dotnet add package Optimizer.Tiff
```

The optimizer declares Windows support because its current project/tooling path is Windows-targeted. The underlying TIFF format package remains part of `Hawkynt.FileFormats.Images`.

## ✨ Features

- Reads TIFF through the shared image-format registry and preserves source image metadata.
- Searches uncompressed, PackBits, LZW, DEFLATE, and Ultra/Hyper DEFLATE variants.
- Searches no predictor versus horizontal differencing.
- Auto-selects compatible original, grayscale, and palette color modes.
- Searches configurable rows-per-strip values and can derive strip sizes dynamically.
- Optional tiled TIFF candidates with configurable tile sizes.
- Skips PackBits when a quick estimate says it is unlikely to save meaningful space.
- Two-phase screening avoids running expensive Zopfli-class compression for every candidate shape.
- Bounded parallel evaluation with progress and cancellation.

## 🧩 Format / capability support

| Capability | Support | Notes | Reference |
| --- | :---: | --- | --- |
| [TIFF](https://en.wikipedia.org/wiki/TIFF) input | ✅ | Uses the repository's TIFF reader through `FormatRegistry`. | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| TIFF output | ✅ | Returns complete optimized file bytes. | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| Uncompressed | ✅ | Baseline candidate. | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| [PackBits](https://en.wikipedia.org/wiki/PackBits) | ✅ | Skipped when estimated savings are below the optimizer threshold. | [Apple PackBits notes](https://developer.apple.com/library/archive/technotes/tn/tn1023.html) |
| [LZW](https://en.wikipedia.org/wiki/Lempel%E2%80%93Ziv%E2%80%93Welch) | ✅ | TIFF LZW candidate. | [Welch 1984](https://ieeexplore.ieee.org/document/1659158) |
| [DEFLATE](https://en.wikipedia.org/wiki/Deflate) | ✅ | Standard plus Ultra/Hyper search modes. | [RFC 1951](https://www.rfc-editor.org/rfc/rfc1951) |
| Horizontal differencing predictor | ✅ | Tested against no predictor. | [TIFF Predictor technical note](https://www.awaresystems.be/imaging/tiff/tifftags/predictor.html) |
| Strips | ✅ | Configurable/dynamic rows per strip. | [TIFF strips](https://www.awaresystems.be/imaging/tiff/tifftags/rowsperstrip.html) |
| Tiles | ✅ | Optional `TryTiles` path with configurable square tile sizes. | [TIFF tiles](https://www.awaresystems.be/imaging/tiff/tifftags/tilewidth.html) |
| Multi-page optimization | ⚠️ | The optimizer constructor operates on one decoded `RawImage`; package-level TIFF reading supports multi-page files separately. | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |

## 🚀 Quick start

```csharp
using Optimizer.Tiff;

var optimizer = TiffOptimizer.FromFile(new FileInfo("input.tif"));
var result = await optimizer.OptimizeAsync();

File.WriteAllBytes("output.tif", result.FileContents);
Console.WriteLine(result);
```

### Enable tiled candidates

```csharp
var options = new TiffOptimizationOptions(
  TryTiles: true,
  TileSizes: [64, 128, 256],
  EnableTwoPhaseOptimization: true,
  Phase2CandidateCount: 8);

var result = await TiffOptimizer.FromFile(new FileInfo("input.tif"), options).OptimizeAsync();
```

## 📚 Options

| Option | Default | Purpose |
| --- | --- | --- |
| `Compressions` | None, PackBits, Lzw, Deflate, DeflateUltra | Compression candidates. |
| `Predictors` | None, HorizontalDifferencing | Predictor candidates. |
| `StripRowCounts` | `1, 8, 16, 64` | Explicit rows-per-strip candidates. |
| `AutoSelectColorMode` | `true` | Include grayscale/palette forms when compatible. |
| `DynamicStripSizing` | `true` | Add source-dependent strip sizes. |
| `TryTiles` | `false` | Include tiled TIFF layouts. |
| `TileSizes` | `64, 128, 256` | Square tile-size candidates. |
| `MaxParallelTasks` | CPU count | Bound concurrent candidate evaluation. |
| `ZopfliIterations` | `15` | Iterations for expensive DEFLATE search. |
| `EnableTwoPhaseOptimization` | `true` | Screen candidate shapes before Ultra/Hyper compression. |
| `Phase2CandidateCount` | `5` | Best candidate shapes promoted to expensive compression. |

## 🏗️ Architecture

The optimizer extracts the source `RawImage` once, records useful statistics (grayscale state, unique-color count, sample depth, photometric interpretation), and uses those facts to avoid generating impossible or obviously pointless combinations.

Candidate shape is the combination of compression, predictor, color mode, and strip/tile geometry. Expensive DEFLATE variants are screened by evaluating their candidate shape with ordinary DEFLATE first; only the best shapes are promoted to Ultra/Hyper in phase two.

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| [`Hawkynt.FileFormats.Images`](../../Hawkynt.FileFormats.Images/README.md) | TIFF reader/registry and shared image package. |
| `FileFormat.Core` | `RawImage` and image metadata. |
| `FileFormat.Tiff` | TIFF enums/writer primitives. |
| `Crush.Core` | Shared optimizer progress/infrastructure. |
| `BitMiracle.LibTiff.NET` | TIFF interoperability used by the format implementation. |

## ⚠️ Limitations

- The optimizer's public constructor operates on one `RawImage`; optimizing every page of a multi-page TIFF requires page-aware orchestration by the caller.
- `TryTiles` is off by default because tiled search adds a substantial geometry dimension.
- Ultra/Hyper DEFLATE deliberately trades CPU time for file size. Keep two-phase screening enabled unless exhaustive CPU cost is acceptable.
- Color-mode reduction is constrained by the decoded source; the optimizer does not silently invent a lossy quantization step for images with more colors than a palette can represent.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../../LICENSE).
