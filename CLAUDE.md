# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Build individual projects
dotnet build PngCrush/PngCrush.csproj -c Release
dotnet build PngOptimizer/PngOptimizer.csproj -c Release
dotnet build Compression.Core/Compression.Core.csproj -c Release
dotnet build GifOptimizer/GifOptimizer.csproj -c Release
dotnet build GifCrush/GifCrush.csproj -c Release
dotnet build TiffOptimizer/TiffOptimizer.csproj -c Release
dotnet build TiffCrush/TiffCrush.csproj -c Release

# Run all tests
dotnet test Compression.Tests/Compression.Tests.csproj
dotnet test PngOptimizer.Tests/PngOptimizer.Tests.csproj
dotnet test GifOptimizer.Tests/GifOptimizer.Tests.csproj
dotnet test TiffOptimizer.Tests/TiffOptimizer.Tests.csproj

# Run tests with coverage
dotnet test PngOptimizer.Tests/PngOptimizer.Tests.csproj --collect:"XPlat Code Coverage"
dotnet test Compression.Tests/Compression.Tests.csproj --collect:"XPlat Code Coverage"

# Run specific test category
dotnet test PngOptimizer.Tests/PngOptimizer.Tests.csproj --filter "TestCategory=Regression"
dotnet test PngOptimizer.Tests/PngOptimizer.Tests.csproj --filter "TestCategory=Performance"

# Run the CLI tools
dotnet run --project PngCrush -- -i <input.png> -o <output.png>
dotnet run --project GifCrush -- -i <input.gif> -o <output.gif>
dotnet run --project TiffCrush -- -i <input.tiff> -o <output.tiff>
```

## Architecture

Twelve-project solution across two repos: compression library, three format-specific optimizer libraries, three CLI wrappers, and five test projects.

**Compression.Core** (net8.0, library) — pure RFC 1951 DEFLATE compression library with no PNG or platform dependencies. Contains the Zopfli-class encoder (`ZopfliDeflater`) reusable by any project needing high-ratio DEFLATE compression. No `System.Drawing.Common` or Windows dependency.

**PngOptimizer** (net10.0-windows, library) — the core PNG optimization engine. References `Compression.Core` and `FrameworkExtensions.System.Drawing`. Takes a `System.Drawing.Bitmap`, analyzes pixel data via unsafe pointer access, generates all viable `OptimizationCombo` permutations (color mode x bit depth x filter strategy x deflate method x interlace method x optional quantizer/ditherer combo), compresses each in parallel using `SemaphoreSlim`, and returns the smallest valid result as `OptimizationResult`.

**PngCrush** (net10.0-windows, console app) — thin CLI using `CommandLineParser`. Loads a PNG, constructs `PngOptimizationOptions`, calls `PngOptimizer.OptimizeAsync()`, and writes the result. Wires `Console.CancelKeyPress` to `CancellationTokenSource`, displays progress via `IProgress<OptimizationProgress>`, validates output directory exists.

**GifOptimizer** (net8.0-windows, library) — GIF optimization engine. References `GifFileFormat` from the AnythingToGif repo. Parses input GIF, generates palette reorder/frame optimization combos, tests in parallel, and returns the smallest result.

**GifCrush** (net9.0-windows, console app) — async CLI for GIF optimization using `CommandLineParser`. Wires `Console.CancelKeyPress` to `CancellationTokenSource`, displays progress via `IProgress<GifOptimizationProgress>`, validates output directory exists.

**TiffOptimizer** (net8.0, library) — TIFF optimization engine. References `Compression.Core` and `BitMiracle.LibTiff.NET`. Supports PackBits, LZW, DEFLATE (with Zopfli Ultra/Hyper), horizontal differencing predictor, and color mode reduction.

**TiffCrush** (net9.0, console app) — async CLI for TIFF optimization using `CommandLineParser`. Wires `Console.CancelKeyPress` to `CancellationTokenSource`, displays progress via `IProgress<TiffOptimizationProgress>`, validates output directory exists.

**Compression.Tests** (net8.0, NUnit 4) — tests for `ZopfliDeflater`: BitWriter, symbol tables, Huffman trees, hash chain, round-trip compression, compression ratios, convergence detection.

**PngOptimizer.Tests** (net10.0-windows, NUnit 4) — comprehensive test suite with unit, regression, end-to-end, performance, and ditherer expansion tests for PNG optimization. Uses `StressTest.png` from `Fixtures/` as integration test fixture.

**GifOptimizer.Tests** (net8.0-windows, NUnit 4) — GIF reader tests (manual GIF construction), LZW round-trip, palette reorderer, frame optimizer, LZW compressor, end-to-end tests.

**TiffOptimizer.Tests** (net8.0, NUnit 4) — PackBits round-trip, TIFF optimizer integration, end-to-end tests with LibTiff readback.

### Key Types in Compression.Core

- `ZopfliDeflater` (sealed partial) — custom Zopfli-class DEFLATE encoder producing standard zlib-wrapped output. Uses direct O(1) lookup tables for length/distance code mapping, distance-aware lazy matching in greedy parse (compares estimated bit cost of emitting current match vs literal+next match using fixed Huffman code lengths via `_EstimateLiteralCost`/`_EstimateMatchCost`), adaptive hash chain depth based on local data entropy (64-byte window diversity thresholds), multi-length DP optimal parsing with ArrayPool-backed DP arrays and count-then-fill traceback, iterative refinement with convergence detection (early exit when parse stabilizes), cached fixed Huffman trees, pre-reversed Huffman codes for fast LSB-first output, cost-only arithmetic block measurement (no throwaway BitWriter passes), ArrayPool-backed greedy parse, cached RLE encoding via `DynamicHeader` struct (computes RLE once in `_BuildDynamicHeader`, reuses for both measurement and writing), and Huffman-cost block splitting with statistical candidate detection (sliding window L1 frequency divergence) plus arithmetic measurement and fixed/dynamic selection per block. Organized as nested types: `BitWriter` (LSB-first bit output with bulk byte-aligned writes and smarter buffer growth), `HashChain` (LZ77 match finder with Knuth multiplicative hash, secondary-byte quick reject to skip ~50% more false positives, and adaptive depth via `EstimateLocalDepth`), `HuffmanTree` (tree-based Package-Merge length-limited Huffman with O(n) item storage instead of O(n^2) coverage arrays, precomputed reversed codes), `OptimalParser` (forward-DP shortest-path with multi-length expansion, pooled DP arrays, and adaptive hash chain depth), `BlockSplitter` (Huffman-tree-cost DP block splitting with statistical candidate detection via sliding window frequency divergence). Ultra mode: 2-pass DP with dual hash chain depths. Hyper mode: parallel hash chain construction, starts from Ultra result, N-iteration refinement with convergence detection + block splitting with per-block reparse (ArrayPool-backed sub-arrays, re-optimizes each block's LZ77 parse using block-specific Huffman trees) + arithmetic measurement, always picks the smallest of (Ultra single-block, Hyper single-block, Hyper block-split).

### Key Types in PngOptimizer

- `PngOptimizer` (partial, sealed) — main engine. `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)` is the public entry point. Supports cancellation via `CancellationToken` (propagated to `SemaphoreSlim.WaitAsync` and checked between phases) and progress reporting via `IProgress<OptimizationProgress>` (thread-safe combo count and best size tracking with `Interlocked`). Validates constructor input (`ArgumentNullException.ThrowIfNull`). Handles pixel conversion, palette quantization (alpha-aware with two-tier frequency sort), FrameworkExtensions dithering dispatch, tRNS chunk generation (RGB key color and Grayscale key color for binary alpha images), PNG chunk assembly, CRC32 checksums. Partial classes: `ArgbPixel` (pixel struct), `PooledMemoryStream` (expandable stream wrapper).
- `OptimizationProgress` (readonly record struct) — progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar` (long), `Phase` ("Screening"/"Optimizing"/"Complete").
- `MedianCutQuantizer` — median-cut color quantization algorithm. Builds a histogram, splits color-space boxes along the widest axis at the median frequency, and produces a reduced palette. Used when `AllowLossyPalette` is enabled and the image has >256 unique colors.
- `QuantizerDithererCombo` (readonly record struct) — identifies a quantizer/ditherer pair by name for FrameworkExtensions-based lossy quantization.
- `PngChunkReader` — parses PNG byte stream, extracts ancillary chunks categorized by insertion point (before PLTE, between PLTE and IDAT, after IDAT). Used for chunk preservation.
- `FilterTools` — static implementations of the 5 PNG row filters (None, Sub, Up, Average, Paeth). SIMD-accelerated Sub, Up, Average, and Paeth filters via `System.Numerics.Vector<byte>` (Paeth uses `Vector<ushort>` widening for signed arithmetic) with scalar fallback. Uses `ArrayPool<byte>` for temporary buffers.
- `PngFilterOptimizer` — applies a chosen `FilterStrategy` across all scanlines. Supports `SingleFilter`, `ScanlineAdaptive`, `WeightedContinuity`, `BruteForce` (compression-verified), and `BruteForceAdaptive` (per-scanline with 16-row lookahead compression for ambiguous rows where top-2 scores are within 15%).
- `PngFilterSelector` — heuristic-based per-scanline filter selection. Deflate-aware scoring (`CalculateDeflateAwareScore`) with zero-run quadratic bonus, non-zero run bonus (runLength^1.5 for runs ≥4), byte-pair frequency bonus for LZ77 matching, high-diversity penalty (16-byte windows with >14 unique values), and circular distance for filter residuals. Supports weighted continuity, early break on perfect zero rows, stickiness optimization for spatially consistent content.
- `PngPaletteReorderer` — palette reordering strategies for indexed PNG images: HilbertCurve (3D color-space Z-order), SpatialLocality (first-occurrence order), DeflateOptimized (tries each ordering, compresses 16-row sample, picks smallest).
- `ImageStats` (readonly record struct) — pixel statistics: `UniqueColors`, `UniqueArgbColors`, `HasAlpha`, `IsGrayscale`, `TransparentKeyColor` (RGB key for binary alpha), `TransparentKeyGray` (grayscale key for binary alpha).
- `ImagePartitioner` — content-aware row partitioning for the `PartitionOptimized` filter strategy.
- `Adam7` — static helper with Adam7 interlace pass definitions per PNG spec.
- `OptimizationCombo` (readonly record struct) — one combination of `ColorMode`, bit depth, `FilterStrategy`, `DeflateMethod`, `InterlaceMethod`, optional `QuantizerDithererCombo`.
- `OptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and optional `LossyPaletteCombo`.
- `PngOptimizationOptions` (sealed record) — user-facing configuration (includes `AllowLossyPalette`, `UseDithering`, `QuantizerNames`, `DithererNames`, `IsHighQualityQuantization`, `PreserveAncillaryChunks`, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`, `OptimizePaletteOrder`).

### Key Types in GifOptimizer

- `GifOptimizer` — main engine with `FromFile()` (input validation: null check, file existence, corrupt file wrapping), `OptimizeAsync(CancellationToken, IProgress<GifOptimizationProgress>?)`, parallel combo testing. Combo axes include palette strategy, frame differencing, compression-aware disposal, deferred LZW clear codes.
- `GifOptimizationProgress` (readonly record struct) — progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar` (long), `Phase`.
- `PaletteReorderer` — implements 7 palette reorder strategies (Original, FrequencySorted, LuminanceSorted, SpatialLocality, LzwRunAware, HilbertCurve, CompressionOptimized). CompressionOptimized brute-forces all heuristic orderings via actual LZW compression.
- `LzwCompressor` — standalone LZW encoder with deferred clear code support (defers table reset until compression ratio degrades, adaptive check interval starting at 64 doubling to 1024). Uses an 8192-slot open-addressing hash table with generation counter for O(1) table resets.
- `GifFrameDifferencer` — computes pixel differences between consecutive frames, replacing unchanged pixels with transparent index for better LZW compression.
- `GifFrameOptimizer` — frame disposal optimization (including compression-aware greedy forward pass), transparent margin trimming, frame deduplication (palette-aware: resolves effective palettes and compares visual output for frames with different palette orderings or GCT vs LCT), GCT vs LCT selection.
- `GifAssembler` — assembles complete GIF byte stream from optimized components.
- `GifOptimizationOptions` (sealed record) — configuration: palette strategies, disposal optimization, margin trimming, frame differencing, deferred clear codes, deduplication, parallelism, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`.

### Key Types in TiffOptimizer

- `TiffOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<TiffOptimizationProgress>?)`, parallel combo testing. Supports RGB, Grayscale, Palette, BiLevel color modes. Features palette frequency sorting, PackBits cost estimation, dynamic strip sizing, and tiled encoding support.
- `TiffOptimizationProgress` (readonly record struct) — progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar` (long), `Phase`.
- `PackBitsCompressor` — internal PackBits RLE encoder/decoder with `EstimateCompressionRatio` for pre-filtering ineffective combos.
- `TiffAssembler` — TIFF byte stream assembly via LibTiff.NET with custom raw strip/tile writing for Zopfli DEFLATE integration. Supports both strip-based and tile-based output.
- `TiffOptimizationOptions` (sealed record) — configuration: compressions, predictors, strip row counts, dynamic strip sizing, tile support, auto color mode, Zopfli iterations, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`.

### Optimization Pipeline (PNG)

1. Constructor validates input (`ArgumentNullException.ThrowIfNull`), extracts ARGB pixel data, and computes `ImageStats` (unique colors, unique ARGB colors, alpha presence, grayscale detection, transparent key color for binary alpha). Stores source `Bitmap` reference for dithering.
2. `_GenerateCombinations()` determines which color modes to try. For grayscale images with binary alpha and a single transparent gray value, also generates Grayscale+tRNS combos. For palette mode with >256 colors, generates `QuantizerDithererCombo` entries when `UseDithering` is enabled.
3. `OptimizeAsync()` pre-computes pixel conversions once per `(ColorMode, BitDepth, QuantizerDithererCombo?)` group. For dithered palette combos, `_QuantizeWithFrameworkExtensions` calls `ReduceColors<TQ, TD>` via a two-level type dispatch (quantizer then ditherer), extracts palette and pixel indices from the resulting indexed bitmap. Palette images with `OptimizePaletteOrder` enabled are reordered via `PngPaletteReorderer.DeflateOptimizedSort`.
4. For Adam7 interlaced combos, `_ExtractAdam7SubImages` extracts 7 sub-images from already-converted scanlines.
5. Two-phase optimization (when enabled and expensive methods present): Phase 1 screens all combos with Maximum compression, ranks by size, takes top N candidates. Phase 2 re-tests only those candidates with expensive Ultra/Hyper methods. Phase 1 results are also included as candidates to ensure fast compression is never worse than expensive.
6. Each combo is tested in parallel: filtered, compressed, assembled into a PNG byte stream.
7. Best result selected across all phases.

### Platform Notes

- PngOptimizer uses `System.Drawing.Common` and `FrameworkExtensions.System.Drawing` which require Windows and the `-windows` TFM.
- GifOptimizer uses `System.Drawing.Common` and references GifFileFormat (net8.0-windows).
- TiffOptimizer uses `System.Drawing.Common` with `EnableWindowsTargeting` but targets plain `net8.0`.
- Compression.Core has no platform dependencies (pure BCL).

## Test Infrastructure

**Compression.Tests** (NUnit 4, 51 tests) contains:
- `ZopfliDeflaterTests` — BitWriter bit packing, RFC 1951 symbol tables, lookup table verification for all lengths/distances, Huffman tree construction, hash chain matching (including secondary-byte quick reject), round-trip compression (Ultra/Hyper), compression ratio vs .NET SmallestSize, multi-length DP validation, convergence detection, 64KB+ round-trip tests, lazy matching validation (including distance-aware cost comparison), adaptive depth tests, statistical block split candidate detection, block reparse validation, RLE caching verification

**PngOptimizer.Tests** (NUnit 4, 184 tests) contains:
- `FilterToolsTests` — filter correctness for all 5 filter types, multi-row, edge cases, SIMD Paeth verification
- `DataTypeTests` — enum values match PNG spec, record struct layouts, null bitmap validation
- `PngFilterSelectorTests` — SAD calculation, palette/grayscale heuristics, deflate-aware scoring (byte-pair repetition, high-diversity penalty), early break, stickiness
- `PngFilterOptimizerTests` — all filter strategies including BruteForce and BruteForceAdaptive, palette/grayscale shortcuts
- `PngPaletteReordererTests` — Hilbert sort, spatial locality sort, deflate-optimized sort, identity order
- `ImagePartitionerTests` — partition behavior, filter type ranges
- `Adam7Tests` — Adam7 pass dimension calculations, interlaced PNG generation and readback for RGB/Palette/Grayscale/1x1
- `EndToEndTests` — full optimize->readback with pixel equality, all color modes, interlacing with readback, tRNS transparency (RGB and Grayscale), RGBA-to-RGB+tRNS, Grayscale+tRNS for binary alpha, BruteForce E2E, chunk preservation, two-phase optimization, CancellationToken, IProgress reporting
- `MedianCutQuantizerTests` — median-cut quantization accuracy, palette size, alpha handling, lossy palette E2E
- `DithererExpansionTests` — FrameworkExtensions quantizer/ditherer integration (all 5 quantizers, 5 ditherers, high-quality mode, error handling, E2E readback)
- `RegressionTests` — `[Category("Regression")]` tests for each fixed bug
- `PerformanceTests` — `[Category("Performance")]` throughput and timeout tests

**GifOptimizer.Tests** (NUnit 4, 71 tests) contains:
- `ReaderTests` — parse GIF87a/89a, header, frames, LCT/GCT, interlace, transparency (manual GIF construction)
- `LzwRoundTripTests` — encode with LzwCompressor, decode with Reader, pixel-perfect match
- `PaletteReordererTests` — valid permutation, remap+inverse=identity, frequency sort correctness, all 7 strategies including CompressionOptimized
- `GifFrameOptimizerTests` — disposal optimization (including compression-aware), margin trimming, frame differencing, frame deduplication, palette-aware dedup regression tests (swapped palettes, GCT vs LCT)
- `LzwCompressorTests` — compression correctness, deferred clear codes, edge cases, hash table optimization, adaptive clear interval
- `EndToEndTests` — optimize->readback, all strategies, input validation (null/missing/corrupt file), CancellationToken

**TiffOptimizer.Tests** (NUnit 4, 36 tests) contains:
- `PackBitsCompressorTests` — round-trip, edge cases, large data, compression ratio estimation
- `TiffOptimizerTests` — combo generation, color mode detection, all compression methods, palette frequency sorting, dynamic strip sizing, tile support
- `EndToEndTests` — optimize->readback via LibTiff, pixel equality, tiled TIFF readback, input validation (null/missing file, null bitmap), CancellationToken

Test fixtures: `PngOptimizer.Tests/Fixtures/StressTest.png` (copied from `PngCrush/Examples/`).
