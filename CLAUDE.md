# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Build a single project (pattern: dotnet build <ProjectDir>/<ProjectDir>.csproj)
dotnet build FileFormat.Qoi/FileFormat.Qoi.csproj -c Release
dotnet build Optimizer.Png/Optimizer.Png.csproj -c Release
dotnet build Compression.Core/Compression.Core.csproj -c Release

# Run a single test project
dotnet test FileFormat.Qoi.Tests/FileFormat.Qoi.Tests.csproj
dotnet test Compression.Tests/Compression.Tests.csproj

# Run a specific test by name
dotnet test FileFormat.Qoi.Tests/FileFormat.Qoi.Tests.csproj --filter "FullyQualifiedName~RoundTrip"

# Run a specific test category (Optimizer.Png.Tests uses categories)
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --filter "TestCategory=Regression"
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --filter "TestCategory=Performance"

# Run tests with coverage
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --collect:"XPlat Code Coverage"

# Run a CLI optimizer tool
dotnet run --project Crush.Png -- -i <input.png> -o <output.png>
dotnet run --project Crush.Jpeg -- -i <input.jpg> -o <output.jpg>
# Pattern: dotnet run --project Crush.<Format> -- -i <input> -o <output>
# Available: Crush.Png, Crush.Gif, Crush.Tiff, Crush.Bmp, Crush.Tga, Crush.Pcx,
#            Crush.Jpeg, Crush.Ico, Crush.Cur, Crush.Ani, Crush.WebP
```

**Test project locations**: Most test projects are at `<ProjectName>.Tests/<ProjectName>.Tests.csproj` at the repo root. Newer test projects live under `Tests/<ProjectName>.Tests/`.

## Architecture

~1100-project solution organized in a modern `.slnx` file with solution folders: `/FileFormats/` (542 FileFormat.\*), `/Optimizers/` (11 Optimizer.\*), `/Image/` (Optimizer.Image + Crush.Image), `/Tests/` (~557 test projects + Crush.TestUtilities), and root (Compression.Core, Crush.Core).

### Project Layers

```
Crush.<Format>  (CLI apps — thin wrappers using CommandLineParser + CrushRunner)
       │
Optimizer.<Format>  (optimization engines — parallel combo testing)
       │
FileFormat.<Format>  (format I/O — reader/writer pairs)
       │
FileFormat.Core  (shared interfaces, PixelConverter, RawImage, block decoders)
Compression.Core  (Zopfli-class DEFLATE — no platform dependencies)
Crush.Core  (shared CLI utilities — CrushRunner, OptimizationProgress)
```

### Core Libraries

**Compression.Core** (net8.0) — Pure RFC 1951 DEFLATE. `ZopfliDeflater` with Ultra mode (2-pass DP, dual hash chain depths) and Hyper mode (parallel hash chains, N-iteration refinement with convergence detection, block splitting with per-block reparse). Key nested types: `BitWriter`, `HashChain` (Knuth multiplicative hash, secondary-byte quick reject, adaptive depth), `HuffmanTree` (Package-Merge length-limited), `OptimalParser` (forward-DP shortest-path), `BlockSplitter` (Huffman-tree-cost DP with statistical candidate detection).

**FileFormat.Core** (net8.0) — Shared types: `RawImage` (platform-independent pixel data), `PixelConverter` (SIMD-accelerated, 37 methods including 16-bit paths with Vector128/Vector256), `PixelFormat` enum, `HeaderFieldDescriptor` (hex editor field coloring), `FormatCapability` flags. Block decoders in `BlockDecoders/`: BC1-BC7, ETC1/ETC2, ASTC, PVRTC. Defines the core interfaces (see Coding Patterns below).

**Crush.Core** (net8.0) — `CrushRunner` (generic `RunAsync<TResult>` handling file validation, cancellation, progress, timing), `ICrushOptions`, `OptimizationProgress` (readonly record struct), `FileFormatting`.

**Crush.TestUtilities** (net8.0) — `TestBitmapFactory` (reproducible test bitmaps) and `TempFileScope` (IDisposable temp file lifecycle).

### Optimizer Libraries (formats with dedicated optimization)

Each optimizer follows the same strategy: generate every valid combination of encoding parameters, compress each in parallel via `SemaphoreSlim`, and keep the smallest result. All support `CancellationToken` and `IProgress<OptimizationProgress>`.

| Optimizer | TFM | Combo Axes | Key Features |
|-----------|-----|-----------|--------------|
| **Optimizer.Png** | net10.0-windows | color mode x bit depth x filter strategy x deflate method x interlace x quantizer/ditherer | Two-phase optimization, SIMD filters, median-cut quantization, palette reordering (Hilbert/SpatialLocality/DeflateOptimized), tRNS key color |
| **Optimizer.Gif** | net8.0-windows | palette strategy x color table x disposal x margin trimming x frame differencing x deferred clear codes | LZW with deferred clear codes, frame deduplication (palette-aware), compression-aware disposal |
| **Optimizer.Tiff** | net8.0 | color mode x compression x predictor x strip/tile size | PackBits/LZW/DEFLATE/Zopfli, horizontal differencing predictor, dynamic strip sizing, tiled encoding |
| **Optimizer.Bmp** | net8.0 | color mode(7) x compression x row order | RLE8/RLE4, palette frequency sorting |
| **Optimizer.Tga** | net8.0 | color mode(5) x compression x origin | Pixel-width-aware RLE |
| **Optimizer.Pcx** | net8.0 | color mode(5) x plane config x palette order | Single-plane/separate-planes |
| **Optimizer.Jpeg** | net8.0 | mode x quality x subsampling x Huffman x metadata | Lossless (DCT coefficient rewrite) + lossy (re-encode) |
| **Optimizer.Ico** | net8.0 | BMP vs PNG per entry (2^n, capped 256) | Multi-entry format optimization |
| **Optimizer.Cur** | net8.0 | BMP vs PNG per entry (2^n, capped 256) | Hotspot coordinate preservation |
| **Optimizer.Ani** | net8.0 | BMP vs PNG per entry across frames (2^n, capped 256) | Preserves RIFF ACON structure, rates, sequence |
| **Optimizer.WebP** | net8.0 | metadata stripping | Container-level only (no pixel re-encode) |

### FileFormat Libraries (542 format-specific libraries)

All 542 FileFormat.\* libraries follow a uniform pattern (see Coding Patterns). Notable exceptions to the simple pattern:

- **FileFormat.Png** — References Compression.Core. PngWriter has internal fast path for pre-filtered/pre-compressed data used by Optimizer.Png.
- **FileFormat.WebP** — Full VP8L lossless + VP8 lossy pixel codecs (not just container parsing). References FileFormat.Riff.
- **FileFormat.Tiff**, **FileFormat.Jpeg** — Use BitMiracle wrapper NuGet libraries (LibTiff.NET, LibJpeg.NET).
- **FileFormat.Pdf** — PDF embedded image extractor with xref parsing, stream filter decoding (FlateDecode, DCTDecode, CCITTFaxDecode, ASCII85Decode, ASCIIHexDecode).
- **FileFormat.WindowsPe** — PE resource extractor for icons/cursors/bitmaps from EXE/DLL .rsrc sections.
- **FileFormat.Cur** — Reuses FileFormat.Ico internals via `InternalsVisibleTo`.
- **FileFormat.Ani** — RIFF ACON container, uses FileFormat.Riff and FileFormat.Ico.
- **Multi-image formats** (implement `IMultiImageFileFormat<T>`): IcoFile, CurFile, AniFile, ApngFile, MngFile, FliFile, DcxFile, MpoFile, IcnsFile, TiffFile, BigTiffFile.

## Coding Patterns & Conventions

### C# Project Settings (all projects)

- `ImplicitUsings=disable` — all imports are explicit
- `Nullable=enable`
- `LangVersion=preview`
- `AllowUnsafeBlocks=true`
- File-scoped namespaces: `namespace FileFormat.Qoi;`
- License: LGPL-3.0-or-later

### FileFormat Library Pattern

Each FileFormat.\* library contains these files following a strict naming convention:

```
FileFormat.<Fmt>/
  <Fmt>File.cs       — readonly record struct data model, implements interfaces
  <Fmt>Reader.cs     — static class: FromFile(), FromStream(), FromSpan(), FromBytes()
  <Fmt>Writer.cs     — static class: ToBytes()
  <Fmt>Header.cs     — readonly partial record struct with [GenerateSerializer]
  <Fmt>*.cs           — enums, codecs, compressors as needed
```

**Data model** (`<Fmt>File`): Always a `readonly record struct` implementing a combination of:
- `IImageFormatReader<TSelf>` — static abstract `FromSpan(ReadOnlySpan<byte>)`
- `IImageFormatWriter<TSelf>` — static abstract `ToBytes(TSelf)`
- `IImageToRawImage<TSelf>` — static `ToRawImage(TSelf)` converting to platform-independent `RawImage`
- `IImageFromRawImage<TSelf>` — static `FromRawImage(RawImage)` converting from `RawImage`

Uses `[FormatMagicBytes(...)]` attribute for format auto-detection and `[property: HeaderField(...)]` attributes on constructor parameters for hex editor field mapping.

**Header** (`<Fmt>Header`): Uses `[GenerateSerializer]` source generator attribute that auto-generates binary serialization from field layout annotations.

**Project references**: Every FileFormat library references `FileFormat.Core` and `FileFormat.Core.Generators` (as an analyzer for source generation). Uses `InternalsVisibleTo` for the corresponding Optimizer.\* and \*.Tests projects.

### Optimizer Pattern

```
Optimizer.<Fmt>/
  <Fmt>Optimizer.cs            — main engine: FromFile() + OptimizeAsync()
  <Fmt>OptimizationCombo.cs    — readonly record struct: one parameter combination
  <Fmt>OptimizationResult.cs   — readonly record struct: winning combo + file bytes
  <Fmt>OptimizationOptions.cs  — sealed record: user-facing configuration
```

Constructor accepts `Bitmap` or `FileInfo`. `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)` generates all combos, tests in parallel with `SemaphoreSlim`, reports progress via `Interlocked`, returns smallest valid result.

### CLI Pattern

Each Crush.\* app is a thin wrapper: parse options with `CommandLineParser`, call `CrushRunner.RunAsync<TResult>()` which handles file validation, cancellation wiring (`Console.CancelKeyPress`), progress display, timing, and file size reporting.

### Test Pattern

All test projects use **NUnit 4**. Standard test categories per format:
- Reader validation: null, missing file, too small, invalid magic/signature
- Writer output: signature bytes, header fields, data offsets
- Round-trip: read -> write -> read, verify all fields preserved
- Data type tests: enum values, record struct defaults, constants

Optimizer test projects additionally test: combo generation, E2E optimize->readback, cancellation, progress reporting.

**Test fixture**: `Optimizer.Png.Tests/Fixtures/StressTest.png` (copied from `Crush.Png/Examples/`).

### Internal Method Naming

Private/internal methods use `_PascalCase` prefix (e.g., `_EstimateLiteralCost`, `_BuildDynamicHeader`).

### Platform Notes

- **Optimizer.Png** uses `System.Drawing.Common` + `FrameworkExtensions.System.Drawing` — requires Windows, targets `net10.0-windows`
- **Optimizer.Gif** — requires Windows, targets `net8.0-windows`
- **Other Optimizer.\*** libraries use `System.Drawing.Common` with `EnableWindowsTargeting=true` but target plain `net8.0`
- **All FileFormat.\*** libraries target `net8.0` with no platform dependencies
- **Compression.Core** — pure BCL, no platform dependencies
- **Crush.\* CLI apps** target `net9.0` or `net10.0-windows` (Crush.Png)
- **FileFormat.Jpeg** uses `BitMiracle.LibJpeg.NET` (note: Chroma422 not supported by this library)
- **FileFormat.Tiff** uses `BitMiracle.LibTiff.NET`

### CI

GitHub Actions workflow (`.github/workflows/Build.yml`): weekly schedule, uses .NET 8/9/10 SDK, matrix build of all Crush.\* CLI projects. Version management via `UpdateVersions.pl`.

### Key Types Quick Reference

| Type | Location | Purpose |
|------|----------|---------|
| `RawImage` | FileFormat.Core | Platform-independent pixel data (Width, Height, Format, PixelData, Palette) |
| `PixelConverter` | FileFormat.Core | SIMD-accelerated format conversion (37 methods) |
| `PixelFormat` | FileFormat.Core | Enum: Gray8, Rgb24, Rgba32, Bgra32, Gray16, Rgb48, Rgba64, Indexed1/4/8, etc. |
| `FormatCapability` | FileFormat.Core | Flags: VariableResolution, MonochromeOnly, IndexedOnly, HasDedicatedOptimizer, MultiImage |
| `ZopfliDeflater` | Compression.Core | Zopfli-class DEFLATE encoder (Ultra/Hyper modes) |
| `CrushRunner` | Crush.Core | Shared CLI optimization runner |
| `OptimizationProgress` | Crush.Core | Readonly record struct: CombosCompleted, CombosTotal, BestSizeSoFar, Phase |
| `PngOptimizer` | Optimizer.Png | PNG optimization engine (partial class) |
| `FilterTools` | Optimizer.Png | SIMD-accelerated PNG row filter implementations |
| `PngFilterSelector` | Optimizer.Png | Deflate-aware heuristic per-scanline filter selection |
| `MedianCutQuantizer` | Optimizer.Png | Median-cut color quantization for lossy palette |
| `LzwCompressor` | Optimizer.Gif | LZW encoder with deferred clear codes |
| `GifFrameOptimizer` | Optimizer.Gif | Frame disposal optimization, margin trimming, deduplication |
| `TestBitmapFactory` | Crush.TestUtilities | Creates reproducible test bitmaps |
| `TempFileScope` | Crush.TestUtilities | IDisposable temp file lifecycle |

## Relationship to CompressionWorkbench

The sibling repo at `../CompressionWorkbench` follows the same architectural philosophy for archive/compression formats. Both repos' `FileFormat.*` assemblies are designed to be independently usable by a future metadata viewer.

### Format Registration

Both repos use **source-generated, zero-reflection registration**:
- **PNGCrushCS**: `FileFormat.Registry.Generator/ImageFormatGenerator.cs` discovers `IImageFormatReader<T>` implementations, generates `ImageFormat.g.cs` (enum) and `FormatRegistration.g.cs` (typed registration calls with magic bytes + priority extracted at compile time)
- **CompressionWorkbench**: `Compression.Registry.Generator/FormatDescriptorGenerator.cs` discovers `IFormatDescriptor` implementations, generates `Format` enum and explicit `new` constructor calls

### Key Diversions

| Aspect | PNGCrushCS | CompressionWorkbench |
|--------|-----------|---------------------|
| Interface style | Static abstract `IImageFormatReader<TSelf>` | Instance-based `IFormatDescriptor` |
| Data model | `readonly record struct` | `sealed class` descriptor |
| Header serialization | `[GenerateSerializer]` source gen | Manual parsing |
| Validation | Readers throw on bad data | 3-tier `IFormatValidator` |
| ImplicitUsings | `disable` | `enable` |

### Shared Patterns

Both use: `FileFormat.*` convention, attribute magic bytes, capability flags, immutable models, `Directory.Build.props`, auto-generated format enums, NUnit 4, LGPL-3.0.
