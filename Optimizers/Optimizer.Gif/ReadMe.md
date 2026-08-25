# Optimizer.Gif

[![NuGet](https://img.shields.io/nuget/v/Optimizer.Gif.svg)](https://www.nuget.org/packages/Optimizer.Gif/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)

> Exhaustive GIF optimization engine for palette order, global/local tables, disposal, transparent-margin trimming, frame differencing, and LZW clear policy.

## 📦 Installation

```bash
dotnet add package Optimizer.Gif
```

## ✨ Features

- Optimizes both static and animated GIF files.
- Searches original, frequency-sorted, luminance-sorted, and LZW-run-aware palette orders.
- Compares global and local color-table layouts where valid.
- Optimizes frame disposal and can test compression-aware disposal choices.
- Trims transparent margins where frames permit it.
- Deduplicates equivalent frames before candidate generation.
- Tests frame differencing for animations.
- Compares standard LZW with deferred-clear behavior using two-phase screening.
- Bounded parallel candidate evaluation with cancellation/progress support.

## 🧩 Format / capability support

| Capability | Support | Notes | Reference |
| --- | :---: | --- | --- |
| [GIF87a / GIF89a](https://en.wikipedia.org/wiki/GIF) input | ✅ | Uses the repository's full GIF parser. | [GIF89a specification](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| GIF output | ✅ | Produces complete optimized file bytes. | [GIF89a specification](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| Animated GIF | ✅ | Frame count, timing, looping and frame-local choices are preserved/optimized as appropriate. | [Netscape loop extension background](http://www.vurdalakov.net/misc/gif/netscape-looping-application-extension) |
| Global color table | ✅ | Tested when a valid shared table can be built. | [GIF89a logical screen descriptor](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| Local color tables | ✅ | Candidate path for per-frame palettes. | [GIF89a image descriptor](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| LZW | ✅ | Standard and deferred-clear policies. | [LZW](https://en.wikipedia.org/wiki/Lempel%E2%80%93Ziv%E2%80%93Welch) / [Welch 1984](https://ieeexplore.ieee.org/document/1659158) |
| Frame differencing | ✅ | Tests delta-style frame payloads for animated images. | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| Transparent-margin trimming | ✅ | Enabled only for frames with transparency. | [GIF89a Graphic Control Extension](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| Color quantization | — | This package optimizes an existing GIF; palette creation/reduction belongs to the image-processing pipeline. | [Color quantization](https://en.wikipedia.org/wiki/Color_quantization) |

## 🚀 Quick start

```csharp
using Optimizer.Gif;

var optimizer = GifOptimizer.FromFile(new FileInfo("input.gif"));
var result = await optimizer.OptimizeAsync();

File.WriteAllBytes("output.gif", result.FileContents);
Console.WriteLine(result);
```

### Tune the search

```csharp
var options = new GifOptimizationOptions(
  TryGlobalColorTable: true,
  TryLocalColorTable: true,
  OptimizeDisposal: true,
  TrimMargins: true,
  TryDeferredClear: true,
  DeduplicateFrames: true,
  TryFrameDifferencing: true,
  TryCompressionAwareDisposal: true,
  EnableTwoPhaseOptimization: true,
  Phase2CandidateCount: 8);

var result = await GifOptimizer.FromFile(new FileInfo("input.gif"), options).OptimizeAsync();
```

## 📚 Options

| Option | Default | Purpose |
| --- | --- | --- |
| `PaletteStrategies` | Original, FrequencySorted, LuminanceSorted, LzwRunAware | Palette-order candidates. |
| `TryGlobalColorTable` | `true` | Try a shared color table if one can represent the animation. |
| `TryLocalColorTable` | `true` | Try per-frame color tables. |
| `OptimizeDisposal` | `true` | Search disposal choices for animated GIFs. |
| `TrimMargins` | `true` | Crop transparent frame margins. |
| `TryDeferredClear` | `true` | Include deferred-clear LZW candidates. |
| `DeduplicateFrames` | `true` | Remove equivalent frames where semantics allow it. |
| `TryFrameDifferencing` | `true` | Encode frame deltas rather than full frames when smaller. |
| `TryCompressionAwareDisposal` | `true` | Include disposal choices informed by resulting compression. |
| `MaxParallelTasks` | CPU count | Bound concurrent candidate evaluation. |
| `EnableTwoPhaseOptimization` | `true` | Screen deferred-clear candidates before the final search. |
| `Phase2CandidateCount` | `5` | Number of best candidate shapes promoted to phase two. |

## 🏗️ Architecture

`GifOptimizer` parses the input into a `GifFile`, optionally deduplicates frames, then generates the Cartesian product of palette strategy, color-table policy, disposal strategy, trimming, LZW policy, and frame differencing that is valid for that file.

Deferred-clear LZW is more expensive, so the default two-phase path first ranks equivalent standard-LZW candidates and promotes only the best shapes. The smallest complete GIF is returned as `GifOptimizationResult.FileContents`.

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| [`Hawkynt.FileFormats.Images`](../../Hawkynt.FileFormats.Images/README.md) | GIF format implementation and shared image package. |
| `FileFormat.Core` | GIF metadata/image primitives and palette strategies. |
| `Crush.Core` | Shared optimizer progress/infrastructure. |

## ⚠️ Limitations

- This package optimizes an already encoded GIF; it does not choose an initial palette from arbitrary true-color input.
- The search space grows with animation complexity and enabled strategies. Bounded parallelism prevents unbounded task creation but cannot make exhaustive search free.
- Some optimizations only apply when transparency or multiple frames make them meaningful; invalid/non-applicable combinations are not forced into the search.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../../LICENSE).
