# Optimizer.Png

[![NuGet](https://img.shields.io/nuget/v/Optimizer.Png.svg)](https://www.nuget.org/packages/Optimizer.Png/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net10.0-blue)

> Exhaustive PNG optimization engine that searches valid color, filter, interlace, palette, and DEFLATE combinations and returns the smallest valid candidate.

## 📦 Installation

```bash
dotnet add package Optimizer.Png
```

## ✨ Features

- Searches PNG color type and bit-depth combinations that are valid for the source image.
- Tries non-interlaced and [Adam7](https://en.wikipedia.org/wiki/Adam7_algorithm) layouts.
- Compares multiple PNG row-filter strategies, including adaptive and partition-aware approaches.
- Compares fast DEFLATE with Ultra/Hyper Zopfli-class modes through a two-phase screening path.
- Optional palette quantization/dithering through the shared FrameworkExtensions color-processing registry.
- Optional ancillary-chunk preservation when the original PNG bytes are supplied.
- Optional palette-order optimization.
- Bounded parallel search with progress and cancellation support.

## 🧩 Format / capability support

| Capability | Support | Notes | Reference |
| --- | :---: | --- | --- |
| [PNG](https://en.wikipedia.org/wiki/PNG) input | ✅ | Optimizes decoded `RawImage`; original bytes may be supplied for ancillary-chunk preservation. | [W3C PNG](https://www.w3.org/TR/png-3/) |
| PNG output | ✅ | Returns complete optimized file bytes. | [W3C PNG](https://www.w3.org/TR/png-3/) |
| Lossless optimization | ✅ | Default path changes representation, not pixels. | [PNG filters](https://www.w3.org/TR/png-3/#9Filter-types) |
| Lossy palette reduction | ⚠️ | Explicit opt-in via `AllowLossyPalette`. | [Color quantization](https://en.wikipedia.org/wiki/Color_quantization) |
| Adam7 interlace | ✅ | Tested as an optional layout. | [PNG interlace](https://www.w3.org/TR/png-3/#8Interlace) |
| PNG filters | ✅ | None/Sub/Up/Average/Paeth are selected through strategy search. | [PNG filter algorithms](https://www.w3.org/TR/png-3/#9Filter-algorithms) |
| DEFLATE | ✅ | Default/Maximum plus Ultra/Hyper search modes. | [RFC 1951](https://www.rfc-editor.org/rfc/rfc1951) |
| Zopfli-class search | ✅ | Expensive candidates can be screened in a second phase. | [Google Zopfli](https://github.com/google/zopfli) |
| 16-bit optimizer pipeline | — | The general format library can read/write high-depth images; the optimizer pipeline is currently 8-bit oriented. | [PNG sample depth](https://www.w3.org/TR/png-3/#11IHDR) |

## 🚀 Quick start

```csharp
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Optimizer.Png;

var input = new FileInfo("input.png");
var original = File.ReadAllBytes(input.FullName);
var raw = FormatRegistry.Read(input)
  ?? throw new InvalidDataException("Not a readable image.");

var optimizer = new PngOptimizer(
  raw,
  original,
  new PngOptimizationOptions(
    PreserveAncillaryChunks: true,
    EnableTwoPhaseOptimization: true));

var result = await optimizer.OptimizeAsync();
File.WriteAllBytes("output.png", result.FileContents);

Console.WriteLine(result);
```

### Opt in to palette reduction

```csharp
var options = new PngOptimizationOptions(
  AllowLossyPalette: true,
  UseDithering: true,
  MaxPaletteColors: 128,
  IsHighQualityQuantization: true);

var result = await new PngOptimizer(raw, original, options).OptimizeAsync();
```

## 📚 Options

| Option | Default | Purpose |
| --- | --- | --- |
| `AutoSelectColorMode` | `true` | Search compatible PNG color modes/bit depths. |
| `TryInterlacing` | `true` | Include Adam7 candidates. |
| `TryPartitioning` | `true` | Include partition-based filter strategies. |
| `AllowLossyPalette` | `false` | Permit color quantization into indexed PNG. |
| `UseDithering` | `false` | Use configured ditherers during lossy palette reduction. |
| `IsHighQualityQuantization` | `false` | Select higher-quality color-space processing. |
| `MaxPaletteColors` | `256` | Maximum palette size for lossy indexed candidates. |
| `PartitionCount` | `4` | Partition count for partition-aware filtering. |
| `PreserveAncillaryChunks` | `false` | Carry ancillary chunks from supplied original PNG bytes. |
| `MaxParallelTasks` | CPU count | Bound concurrent candidate evaluation. |
| `ZopfliIterations` | `15` | Iteration count for expensive DEFLATE search. |
| `EnableTwoPhaseOptimization` | `true` | Screen candidates cheaply before Ultra/Hyper compression. |
| `Phase2CandidateCount` | `5` | Number of top candidates promoted to expensive compression. |
| `OptimizePaletteOrder` | `true` | Reorder indexed palettes when beneficial. |

Default filter strategies: `SingleFilter`, `ScanlineAdaptive`, `WeightedContinuity`, and `PartitionOptimized`. Default DEFLATE methods: `Default` and `Ultra`.

## 🏗️ Architecture

The optimizer converts a source `RawImage` into candidate PNG pixel layouts once per color/bit-depth/palette configuration, caches those conversions, then evaluates filter/interlace/DEFLATE combinations around them. Adam7 sub-images are cached separately so interlaced candidates do not repeatedly repartition the source.

When expensive compression is enabled, the first phase substitutes a fast compressor for Ultra/Hyper candidates, ranks the results, and only promotes the best candidate shapes into the expensive phase. Cheap candidates remain in the final comparison, so screening does not make “expensive” synonymous with “winner.”

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| [`Hawkynt.FileFormats.Images`](../../Hawkynt.FileFormats.Images/README.md) | PNG format implementation and `FormatRegistry`. |
| `FileFormat.Core` | `RawImage` and pixel conversion primitives. |
| `Compression.Core` | DEFLATE / Zopfli-class compression. |
| `Crush.Core` | Shared optimizer progress/infrastructure. |
| `Hawkynt.ColorProcessing.Adapter` | Quantizer/ditherer bridge used for optional palette reduction. |
| `FrameworkExtensions.Backports` / `FrameworkExtensions.Corlib` | Framework support helpers. |

## ⚠️ Limitations

- Lossless optimization is the default contract; palette reduction is deliberately explicit because it changes pixels.
- Preserving ancillary chunks requires passing the original PNG bytes to `PngOptimizer`.
- Expensive Ultra/Hyper modes trade CPU time for size. Two-phase optimization reduces, not eliminates, that cost.
- This optimizer is not the authoritative PNG decoder/encoder API; use `Hawkynt.FileFormats.Images` when optimization is not required.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../../LICENSE).
