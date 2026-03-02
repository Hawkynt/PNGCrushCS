# PNGCrushCS

![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)
![Language](https://img.shields.io/github/languages/top/Hawkynt/PNGCrushCS?color=purple)
[![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/PNGCrushCS?branch=main)![Activity](https://img.shields.io/github/commit-activity/y/Hawkynt/PNGCrushCS?branch=main)](https://github.com/Hawkynt/PNGCrushCS/commits/main)
[![GitHub release](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Hawkynt/PNGCrushCS/total)](https://github.com/Hawkynt/PNGCrushCS/releases)
[![Build](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/Build.yml/badge.svg)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/Build.yml)

> A C# image optimization suite that reduces file sizes by exhaustively testing combinations of compression parameters and selecting the smallest valid result. Supports **PNG**, **GIF**, and **TIFF** formats through a shared architecture with a custom Zopfli-class DEFLATE encoder.

## How It Works

Each optimizer follows the same strategy: generate every valid combination of encoding parameters, compress each in parallel, and keep the smallest result.

### PNG (PngCrush)

1. Loads a PNG and extracts ARGB pixel data.
2. Analyzes image statistics (unique colors, alpha, grayscale detection, transparent key color).
3. Generates all valid optimization combos (color mode x bit depth x filter strategy x deflate method x interlace x optional quantizer/ditherer).
4. Tests each combo in parallel: convert pixels, apply filters, compress, assemble PNG byte stream.
5. Optional two-phase optimization: screen with fast compression first, then re-test top N candidates with expensive Ultra/Hyper methods.
6. Returns the smallest valid result.

### GIF (GifCrush)

1. Parses input GIF via the `GifFileFormat` reader (header, LSD, GCT, frames with LCT, LZW decode, interlace deinterleaving).
2. Generates combos: palette reorder strategy x color table mode x disposal optimization x margin trimming x frame differencing x deferred clear codes.
3. Tests each combo in parallel: reorder palette, remap pixels, optimize frames, LZW-encode, assemble GIF.
4. Returns the smallest result.

### TIFF (TiffCrush)

1. Opens source TIFF via LibTiff.NET, extracts pixels, analyzes stats.
2. Generates combos: color mode x compression x predictor x strip/tile size (with invalid combo pruning).
3. Tests each combo in parallel: convert pixels, compress strips/tiles (PackBits/LZW/DEFLATE/Zopfli), assemble TIFF.
4. Returns the smallest result.

## Features

### PNG

- **Auto color mode** - Detects grayscale, alpha, unique color count
- **5 color modes** - Grayscale, GrayscaleAlpha, RGB, RGBA, Palette (with sub-byte 1/2/4-bit packing)
- **tRNS transparency** - RGBA-to-RGB+tRNS for binary alpha (saves ~25%), Grayscale+tRNS for grayscale binary alpha (saves ~50%)
- **6 filter strategies** - SingleFilter, ScanlineAdaptive, WeightedContinuity, PartitionOptimized, BruteForce, BruteForceAdaptive
- **SIMD-accelerated filters** - Sub, Up, Average, Paeth via `Vector<byte>` with scalar fallback
- **Deflate-aware filter selection** - Byte-pair frequency bonus, run detection, high-diversity penalty, stickiness optimization
- **6 deflate methods** - Fastest, Fast, Default, Maximum, Ultra (multi-length DP), Hyper (iterative refinement + block splitting)
- **Palette reordering** - Hilbert curve, spatial locality, deflate-optimized orderings
- **Lossy quantization** - Built-in median-cut + FrameworkExtensions quantizer/ditherer combos (Wu, Octree, MedianCut, Neuquant, PngQuant x Floyd-Steinberg, Atkinson, Sierra, Bayer4x4)
- **Adam7 interlacing** - Full support with correct per-pass sub-image generation
- **Ancillary chunk preservation** - tEXt, gAMA, pHYs, sRGB, iCCP
- **Two-phase optimization** - Screen all combos with fast compression, re-test top N with expensive methods

### GIF

- **7 palette reorder strategies** - Original, FrequencySorted, LuminanceSorted, SpatialLocality, LzwRunAware, HilbertCurve, CompressionOptimized
- **Deferred LZW clear codes** - Defers table reset until ratio degrades (3-8% gain), adaptive check interval
- **High-performance LZW** - Open-addressing hash table with generation counter for O(1) resets
- **Frame deduplication** - Palette-aware comparison resolves effective palettes (LCT or GCT), detects duplicates even with different palette orderings
- **Frame differencing** - Replaces unchanged pixels with transparent index (5-15% gain on animations)
- **Compression-aware disposal** - Greedy forward pass testing all disposal methods per frame
- **Transparent margin trimming**, GCT/LCT selection, two-phase optimization

### TIFF

- **4 compression methods** - None, PackBits, LZW, DEFLATE (standard + Zopfli Ultra/Hyper)
- **Horizontal differencing predictor** for LZW and DEFLATE modes
- **Auto color mode** - RGB, Grayscale, Palette, BiLevel
- **Palette frequency sorting**, PackBits cost estimation, dynamic strip sizing
- **Tiled TIFF support** - Configurable tile sizes (multiples of 16)
- **Two-phase optimization** - Screen with standard DEFLATE, re-test top N with Zopfli

### Shared

- **CancellationToken support** - All optimizers; CLIs wire Ctrl+C
- **IProgress reporting** - Real-time combo count, best size, phase updates
- **Parallel processing** - Concurrent combo testing with configurable parallelism
- **Memory-efficient** - `ArrayPool` throughout, concurrent best-result pattern, `stackalloc` for scoring

## Architecture

Twelve-project solution across two repositories:

```
Compression.Core  <-- PngOptimizer  <-- PngCrush
                  <-- TiffOptimizer <-- TiffCrush

GifFileFormat (AnythingToGif repo) <-- GifOptimizer <-- GifCrush

FrameworkExtensions.System.Drawing (NuGet) --> PngOptimizer
BitMiracle.LibTiff.NET (NuGet) --> TiffOptimizer
```

| Project                 | TFM             | Type    | Description                                                                                           |
| ----------------------- | --------------- | ------- | ----------------------------------------------------------------------------------------------------- |
| **Compression.Core**    | net8.0          | Library | Pure RFC 1951 DEFLATE with Zopfli-class optimal parsing. No platform dependencies.                    |
| **PngOptimizer**        | net10.0-windows | Library | PNG optimization engine with unsafe pixel access, FrameworkExtensions dithering integration.          |
| **PngCrush**            | net10.0-windows | Console | CLI wrapper using `CommandLineParser`.                                                                |
| **GifOptimizer**        | net8.0-windows  | Library | GIF optimization engine with palette reordering, frame optimization, LZW re-encoding.                 |
| **GifCrush**            | net9.0-windows  | Console | CLI for GIF optimization.                                                                             |
| **TiffOptimizer**       | net8.0          | Library | TIFF optimization with PackBits, LZW, DEFLATE/Zopfli, tiled encoding.                                 |
| **TiffCrush**           | net9.0          | Console | CLI for TIFF optimization.                                                                            |
| **Compression.Tests**   | net8.0          | NUnit 4 | 51 tests for `ZopfliDeflater`.                                                                        |
| **PngOptimizer.Tests**  | net10.0-windows | NUnit 4 | 184 tests: filters, combos, E2E, ditherers, regression, performance.                                  |
| **GifOptimizer.Tests**  | net8.0-windows  | NUnit 4 | 71 tests: reader, LZW, palette, frames, dedup, E2E.                                                   |
| **TiffOptimizer.Tests** | net8.0          | NUnit 4 | 36 tests: PackBits, optimizer, E2E with LibTiff readback.                                             |
| **GifFileFormat**       | net8.0-windows  | Library | GIF reader/writer with LZW codec (in [AnythingToGif](https://github.com/Hawkynt/AnythingToGif) repo). |

### Key Types

| Type                  | Project          | Purpose                                                                                                                                                                                                                                                            |
| --------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ZopfliDeflater`      | Compression.Core | Zopfli-class DEFLATE encoder: direct lookup tables, distance-aware lazy matching, adaptive hash chain depth, multi-length DP optimal parsing, iterative refinement with convergence detection, Huffman-cost block splitting, cached RLE, ArrayPool-backed reparse. |
| `PngOptimizer`        | PngOptimizer     | Main PNG engine. Pixel conversion, palette quantization, FrameworkExtensions dithering, tRNS generation, PNG assembly.                                                                                                                                             |
| `FilterTools`         | PngOptimizer     | SIMD-accelerated PNG row filters (None, Sub, Up, Average, Paeth).                                                                                                                                                                                                  |
| `PngFilterOptimizer`  | PngOptimizer     | Applies filter strategies across scanlines, including BruteForce with lookahead compression.                                                                                                                                                                       |
| `PngFilterSelector`   | PngOptimizer     | Deflate-aware per-scanline filter selection with byte-pair and run bonuses.                                                                                                                                                                                        |
| `MedianCutQuantizer`  | PngOptimizer     | Built-in median-cut color quantization for lossy palette reduction.                                                                                                                                                                                                |
| `PngPaletteReorderer` | PngOptimizer     | Hilbert, spatial locality, and deflate-optimized palette orderings.                                                                                                                                                                                                |
| `ImagePartitioner`    | PngOptimizer     | Content-aware row partitioning for `PartitionOptimized` strategy.                                                                                                                                                                                                  |
| `PngChunkReader`      | PngOptimizer     | PNG parser for ancillary chunk preservation.                                                                                                                                                                                                                       |
| `Adam7`               | PngOptimizer     | Adam7 interlace pass definitions.                                                                                                                                                                                                                                  |
| `GifOptimizer`        | GifOptimizer     | Main GIF engine. Parses input, generates combos, tests in parallel.                                                                                                                                                                                                |
| `PaletteReorderer`    | GifOptimizer     | 7 palette reorder strategies including CompressionOptimized.                                                                                                                                                                                                       |
| `GifFrameOptimizer`   | GifOptimizer     | Disposal optimization, margin trimming, palette-aware frame deduplication.                                                                                                                                                                                         |
| `GifFrameDifferencer` | GifOptimizer     | Pixel differencing between consecutive frames.                                                                                                                                                                                                                     |
| `LzwCompressor`       | GifOptimizer     | Standalone LZW encoder with deferred clear code support.                                                                                                                                                                                                           |
| `GifAssembler`        | GifOptimizer     | GIF byte stream assembly from optimized components.                                                                                                                                                                                                                |
| `TiffOptimizer`       | TiffOptimizer    | Main TIFF engine. Pixel extraction, combo generation, parallel testing.                                                                                                                                                                                            |
| `PackBitsCompressor`  | TiffOptimizer    | PackBits RLE encoder/decoder with compression ratio estimation.                                                                                                                                                                                                    |
| `TiffAssembler`       | TiffOptimizer    | TIFF assembly via LibTiff.NET with custom Zopfli DEFLATE integration.                                                                                                                                                                                              |
| `Frame`               | GifFileFormat    | Unified GIF frame carrying indexed pixels, palette, delay, disposal, transparency. `FromBitmap` factory for Bitmap-to-indexed conversion.                                                                                                                          |
| `Reader`              | GifFileFormat    | Static GIF parser with LZW decoder and interlace deinterleaving.                                                                                                                                                                                                   |

## Build / Test / Run

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Run all tests
dotnet test Compression.Tests/Compression.Tests.csproj
dotnet test PngOptimizer.Tests/PngOptimizer.Tests.csproj
dotnet test GifOptimizer.Tests/GifOptimizer.Tests.csproj
dotnet test TiffOptimizer.Tests/TiffOptimizer.Tests.csproj

# Run specific tool
dotnet run --project PngCrush -- -i <input.png> -o <output.png>
dotnet run --project GifCrush -- -i <input.gif> -o <output.gif>
dotnet run --project TiffCrush -- -i <input.tiff> -o <output.tiff>
```

## CLI Reference

### PngCrush

| Option              | Short | Default                                            | Description                                            |
| ------------------- | ----- | -------------------------------------------------- | ------------------------------------------------------ |
| `--input`           | `-i`  | *(required)*                                       | Input PNG file path                                    |
| `--output`          | `-o`  | *(required)*                                       | Output PNG file path                                   |
| `--auto-color-mode` | `-a`  | `true`                                             | Auto-select best color mode                            |
| `--interlace`       |       | `true`                                             | Try interlaced (Adam7) encoding                        |
| `--partition`       | `-p`  | `true`                                             | Try smart partitioning                                 |
| `--filters`         | `-f`  | `SingleFilter,ScanlineAdaptive,PartitionOptimized` | Filter strategies                                      |
| `--deflate`         | `-d`  | `Fastest,Default,Ultra`                            | Deflate methods                                        |
| `--lossy-palette`   |       | `false`                                            | Allow lossy palette quantization for >256 color images |
| `--dithering`       |       | `false`                                            | Try multiple quantizer/ditherer combos                 |
| `--quantizers`      |       | `Wu,Octree,MedianCut`                              | Quantizers to try                                      |
| `--ditherers`       |       | `None,FloydSteinberg`                              | Ditherers to try                                       |
| `--hq-quantize`     |       | `false`                                            | High-quality linear RGB color space                    |
| `--preserve-chunks` |       | `false`                                            | Preserve ancillary PNG chunks                          |
| `--jobs`            | `-j`  | `0` (all cores)                                    | Max parallel tasks                                     |
| `--verbose`         | `-v`  | `false`                                            | Verbose output                                         |

### GifCrush

| Option                  | Short | Default                                                | Description                                   |
| ----------------------- | ----- | ------------------------------------------------------ | --------------------------------------------- |
| `--input`               | `-i`  | *(required)*                                           | Input GIF file path                           |
| `--output`              | `-o`  | *(required)*                                           | Output GIF file path                          |
| `--strategies`          | `-s`  | `Original,FrequencySorted,LuminanceSorted,LzwRunAware` | Palette reorder strategies                    |
| `--optimize-disposal`   |       | `true`                                                 | Optimize frame disposal methods               |
| `--trim-margins`        |       | `true`                                                 | Trim transparent margins                      |
| `--deferred-clear`      |       | `true`                                                 | Try deferred LZW clear codes                  |
| `--frame-diff`          |       | `true`                                                 | Try frame differencing                        |
| `--deduplicate`         |       | `true`                                                 | Merge identical consecutive frames            |
| `--compression-palette` |       | `false`                                                | Compression-aware palette reordering (slower) |
| `--jobs`                | `-j`  | `0` (all cores)                                        | Max parallel tasks                            |
| `--verbose`             | `-v`  | `false`                                                | Verbose output                                |

### TiffCrush

| Option              | Short | Default                                  | Description                               |
| ------------------- | ----- | ---------------------------------------- | ----------------------------------------- |
| `--input`           | `-i`  | *(required)*                             | Input TIFF file path                      |
| `--output`          | `-o`  | *(required)*                             | Output TIFF file path                     |
| `--compression`     | `-c`  | `None,PackBits,Lzw,Deflate,DeflateUltra` | Compression methods                       |
| `--predictor`       |       | `None,HorizontalDifferencing`            | Predictors to try                         |
| `--auto-color-mode` | `-a`  | `true`                                   | Auto-detect optimal color mode            |
| `--dynamic-strips`  |       | `true`                                   | Dynamic strip sizes based on image height |
| `--tiles`           |       | `false`                                  | Try tiled TIFF encoding                   |
| `--tile-sizes`      |       | `64,128,256`                             | Tile sizes (multiples of 16)              |
| `--jobs`            | `-j`  | `0` (all cores)                          | Max parallel tasks                        |
| `--verbose`         | `-v`  | `false`                                  | Verbose output                            |

## Planned Features

- ~~CancellationToken and progress reporting support~~ (done)
- ~~Unified GIF Frame type with palette-aware deduplication~~ (done)
- Cross-platform support (replace `System.Drawing.Common` with a portable decoder)

## Known Limitations

- **Windows only** - PngOptimizer and GifOptimizer use `System.Drawing.Common` which requires Windows. Compression.Core and TiffOptimizer have no platform restrictions.
- **Lossy sub-byte modes** - Sub-byte Grayscale (1/2/4-bit) quantizes values, which can lose precision for mid-range grays.
- **No 16-bit support** - Only 8-bit (and sub-byte 1/2/4-bit) depths are supported.
