# PNGCrushCS

![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)
![Language](https://img.shields.io/github/languages/top/Hawkynt/PNGCrushCS?color=purple)
[![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/PNGCrushCS?branch=main)![Activity](https://img.shields.io/github/commit-activity/y/Hawkynt/PNGCrushCS?branch=main)](https://github.com/Hawkynt/PNGCrushCS/commits/main)
[![GitHub release](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Hawkynt/PNGCrushCS/total)](https://github.com/Hawkynt/PNGCrushCS/releases)
[![Build](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/Build.yml/badge.svg)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/Build.yml)

> A C# image optimization suite that reduces file sizes by exhaustively testing combinations of compression parameters and selecting the smallest valid result. Supports **PNG**, **GIF**, **TIFF**, **BMP**, **TGA**, **PCX**, **JPEG**, **ICO**, **CUR**, **ANI**, and **WebP** optimization through a shared architecture with a custom Zopfli-class DEFLATE encoder. Includes **70 file format libraries** covering modern, professional, retro, and exotic image formats.

## How It Works

Each optimizer follows the same strategy: generate every valid combination of encoding parameters, compress each in parallel, and keep the smallest result.

### PNG (Crush.Png)

1. Loads a PNG and extracts ARGB pixel data.
2. Analyzes image statistics (unique colors, alpha, grayscale detection, transparent key color).
3. Generates all valid optimization combos (color mode x bit depth x filter strategy x deflate method x interlace x optional quantizer/ditherer).
4. Tests each combo in parallel: convert pixels, apply filters, compress, assemble PNG byte stream.
5. Optional two-phase optimization: screen with fast compression first, then re-test top N candidates with expensive Ultra/Hyper methods.
6. Returns the smallest valid result.

### GIF (Crush.Gif)

1. Parses input GIF via the `GifFileFormat` reader (header, LSD, GCT, frames with LCT, LZW decode, interlace deinterleaving).
2. Generates combos: palette reorder strategy x color table mode x disposal optimization x margin trimming x frame differencing x deferred clear codes.
3. Tests each combo in parallel: reorder palette, remap pixels, optimize frames, LZW-encode, assemble GIF.
4. Returns the smallest result.

### TIFF (Crush.Tiff)

1. Opens source TIFF via LibTiff.NET, extracts pixels, analyzes stats.
2. Generates combos: color mode x compression x predictor x strip/tile size (with invalid combo pruning).
3. Tests each combo in parallel: convert pixels, compress strips/tiles (PackBits/LZW/DEFLATE/Zopfli), assemble TIFF.
4. Returns the smallest result.

### BMP (Crush.Bmp)

1. Loads a BMP and extracts ARGB pixel data.
2. Analyzes image statistics (unique colors, grayscale detection).
3. Generates combos: color mode x compression x row order (with pruning: RLE8 only for Palette8, RLE4 only for Palette4).
4. Tests each combo in parallel: convert pixels, build palette, apply RLE if applicable, assemble BMP byte stream.
5. Returns the smallest valid result.

### TGA (Crush.Tga)

1. Loads a TGA and extracts ARGB pixel data.
2. Detects alpha channel presence and grayscale content.
3. Generates combos: color mode x compression x origin (with pruning: Indexed8 only when <= 256 colors, Grayscale8 only when grayscale).
4. Tests each combo in parallel: convert pixels (BGRA/BGR/Grayscale/Indexed), apply pixel-width-aware RLE, assemble TGA with optional TGA 2.0 footer.
5. Returns the smallest valid result.

### PCX (Crush.Pcx)

1. Loads a PCX and extracts ARGB pixel data.
2. Analyzes unique color count for palette mode eligibility.
3. Generates combos: color mode x plane config x palette order (with pruning: SeparatePlanes only for RGB24, palette ordering only for indexed modes).
4. Tests each combo in parallel: convert pixels, RLE-encode scanlines (per-plane for RGB), assemble PCX with VGA palette.
5. Returns the smallest valid result.

### JPEG (Crush.Jpeg)

Supports two modes:

**Lossless mode** (default):
1. Reads DCT coefficients from input JPEG via `jpeg_read_coefficients()`.
2. Generates combos: baseline vs progressive x Huffman optimization x metadata stripping.
3. Re-writes coefficients with optimized Huffman tables via `jpeg_write_coefficients()`. No decode-to-pixels, no generation loss.
4. Returns the smallest result.

**Lossy mode** (opt-in via `--lossy`):
1. Decodes to pixels, extracts RGB data with grayscale detection.
2. Generates combos: mode x quality x chroma subsampling.
3. Re-encodes at each quality/subsampling combination via LibJpeg.NET.
4. Returns the smallest result at any acceptable quality.

### ICO (Crush.Ico)

1. Reads an ICO file and parses all image entries (BMP DIB or PNG embedded).
2. Generates 2^n combinations (capped at 256) of BMP vs PNG format per entry.
3. Tests each combination in parallel: reassembles the ICO with the specified formats via IcoWriter.
4. Returns the smallest total file size.

### CUR (Crush.Cur)

1. Reads a CUR file and parses all cursor image entries with hotspot coordinates.
2. CUR format is identical to ICO except type=2 in the header, and directory entry bytes 4-5/6-7 store HotspotX/HotspotY instead of planes/bitCount.
3. Generates 2^n combinations (capped at 256) of BMP vs PNG format per entry, preserving hotspot data.
4. Tests each combination in parallel: reassembles the CUR with the specified formats via CurWriter.
5. Returns the smallest total file size with all hotspot coordinates preserved.

### ANI (Crush.Ani)

1. Reads an ANI animated cursor file (RIFF ACON container) and parses the anih header, optional rate/sequence chunks, and all embedded ICO frames.
2. Generates 2^n combinations (capped at 256) of BMP vs PNG format per image entry across all frames.
3. Tests each combination in parallel: reassembles the ANI with the specified entry formats, preserving rates and sequence data.
4. Returns the smallest total file size.

### WebP (Crush.WebP)

Phase 2 container-level optimization (no pixel encode/decode):

1. Reads a WebP file via the RIFF container parser, extracting VP8/VP8L image data and metadata chunks (ICCP, EXIF, XMP).
2. Generates combos: with metadata vs without metadata (strip EXIF/ICCP/XMP).
3. Tests each combo in parallel: rewrites the RIFF WEBP container with or without metadata chunks.
4. Returns the smallest valid result.

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

### BMP

- **7 color modes** - Original, Rgb24, Rgb16_565, Palette8, Palette4, Palette1, Grayscale8
- **RLE compression** - RLE8 for 8-bit palette, RLE4 for 4-bit palette
- **Row order** - Top-down and bottom-up variants
- **Auto color mode** - Detects grayscale, unique color count, palette eligibility
- **Palette frequency sorting** for optimal RLE runs

### TGA

- **5 color modes** - Original, Rgba32, Rgb24, Grayscale8, Indexed8
- **Pixel-width-aware RLE** - Properly handles 1/3/4 byte pixel widths with max 128-pixel packets
- **Origin variants** - BottomLeft and TopLeft
- **TGA 2.0 footer** - Includes standard "TRUEVISION-XFILE.\0" footer
- **Auto alpha detection** - Detects when alpha channel is unnecessary

### PCX

- **5 color modes** - Original, Rgb24, Indexed8, Indexed4, Monochrome
- **Plane configurations** - SinglePlane (interleaved) and SeparatePlanes (per-component)
- **PCX RLE encoding** - 0xC0-prefix byte-level RLE with max 63-byte runs
- **VGA palette support** - 256-color palette with 0x0C marker
- **Palette ordering** - Original and frequency-sorted orderings

### JPEG

- **Lossless optimization** - DCT coefficient transfer with no generation loss (jpegtran-style)
- **Lossy re-encoding** - Configurable quality levels and chroma subsampling
- **Baseline and Progressive** scan modes
- **Huffman table optimization** - Optimal Huffman coding for smaller output
- **Metadata stripping** - Remove EXIF, ICC profiles, comments
- **Chroma subsampling** - 4:4:4 and 4:2:0 via BitMiracle.LibJpeg.NET
- **Grayscale detection** - Skips subsampling axis for single-component images

### ICO

- **Per-entry format selection** - Independently selects BMP DIB or PNG embedding for each image entry
- **Exhaustive combo search** - Tests all 2^n format combinations (capped at 256)
- **Parallel testing** - Concurrent combo evaluation with configurable parallelism

### CUR

- **Per-entry format selection** - Independently selects BMP DIB or PNG embedding for each cursor image entry
- **Hotspot preservation** - Preserves HotspotX/HotspotY coordinates through optimization
- **Exhaustive combo search** - Tests all 2^n format combinations (capped at 256)
- **Parallel testing** - Concurrent combo evaluation with configurable parallelism

### ANI

- **Per-entry format selection** - Independently selects BMP DIB or PNG embedding for each image entry across all animation frames
- **Structure preservation** - Preserves anih header, rate array, sequence array, and all animation metadata
- **Exhaustive combo search** - Tests all 2^n format combinations (capped at 256) across all frame entries
- **Parallel testing** - Concurrent combo evaluation with configurable parallelism

### WebP

- **Container-level optimization** - RIFF WEBP container rewriting without pixel encode/decode
- **Metadata stripping** - Removes EXIF, ICCP, XMP metadata chunks
- **VP8/VP8L support** - Handles both lossy (VP8) and lossless (VP8L) image data passthrough
- **VP8X extended format** - Reads and writes extended WebP with feature flags (alpha, animation, metadata)
- **Dimension extraction** - Parses VP8 keyframe headers and VP8L bitfield headers for width/height

### Shared

- **CrushRunner** - Common CLI runner (`Crush.Core`) handling input/output validation, cancellation, progress, timing, savings display
- **CancellationToken support** - All optimizers; CLIs wire Ctrl+C
- **IProgress reporting** - Real-time combo count, best size, phase updates
- **Parallel processing** - Concurrent combo testing with configurable parallelism
- **Memory-efficient** - `ArrayPool` throughout, concurrent best-result pattern, `stackalloc` for scoring
- **70 FileFormat libraries** - Standalone reader/writer for each format (see Architecture for full list)
- **FileFormat.Riff** - Shared RIFF container reader/writer used by FileFormat.Ani and FileFormat.WebP
- **FileFormat.Iff** - IFF container reader/writer for Amiga ILBM and related formats
- **FileFormat.Core** - Shared header field descriptor types for hex editor field coloring

## Architecture

~179-project solution across two repositories:

```
Compression.Core  <-- FileFormat.Png  <-- Optimizer.Png  <-- Crush.Png
                  <-- FileFormat.Tiff <-- Optimizer.Tiff <-- Crush.Tiff

GifFileFormat (AnythingToGif repo) <-- Optimizer.Gif <-- Crush.Gif

FileFormat.Bmp  <-- Optimizer.Bmp  <-- Crush.Bmp
FileFormat.Tga  <-- Optimizer.Tga  <-- Crush.Tga
FileFormat.Pcx  <-- Optimizer.Pcx  <-- Crush.Pcx
FileFormat.Jpeg <-- Optimizer.Jpeg <-- Crush.Jpeg

FileFormat.Ico (uses FileFormat.Bmp + FileFormat.Png) <-- Optimizer.Ico <-- Crush.Ico
FileFormat.Cur (uses FileFormat.Ico)                  <-- Optimizer.Cur <-- Crush.Cur

FileFormat.Riff + FileFormat.Ico <-- FileFormat.Ani <-- Optimizer.Ani <-- Crush.Ani
FileFormat.Riff <-- FileFormat.WebP <-- Optimizer.WebP <-- Crush.WebP
FileFormat.Iff <-- FileFormat.Ilbm

FileFormat.Core <-- All FileFormat libraries (header field mapping)

FileFormat.Pcx <-- FileFormat.Dcx (multi-page PCX container)
FileFormat.Png + Compression.Core <-- FileFormat.Apng (animated PNG)
FileFormat.Png <-- FileFormat.Mng (MNG VLC subset with embedded PNG frames)

Standalone FileFormat libraries (reader/writer only, no optimizer yet):
  Qoi, Farbfeld, Wbmp, Netpbm, Xbm, Xpm, MacPaint,
  ZxSpectrum, Koala, Degas, Neochrome, GemImg, AmstradCpc,
  Pfm, Sgi, SunRaster, Hdr, UtahRle, DrHalo, Iff, Ilbm,
  Fli, Cineon, Dds, Vtf, Ktx, Exr, Dpx, Fits, Ccitt,
  BbcMicro, C64Multi, Psd, Hrz, Cmu, Mtv, Qrt, Msp,
  Dcx, Astc, Pkm, Tim, Tim2, Wal, Pvr, Wpg, Bsave,
  Clp, Spectrum512, Tiny, Sixel, Wad, Wad3, Apng, Mng,
  Xcf, Pict, Dicom

Crush.Core <-- All 11 Crush CLIs + All 11 Optimizers
Crush.TestUtilities <-- All 11 Optimizer.Tests

FrameworkExtensions.System.Drawing (NuGet) --> Optimizer.Png
BitMiracle.LibTiff.NET (NuGet) --> FileFormat.Tiff
BitMiracle.LibJpeg.NET (NuGet) --> FileFormat.Jpeg
```

| Project                  | TFM             | Type    | Description                                                                                           |
| ------------------------ | --------------- | ------- | ----------------------------------------------------------------------------------------------------- |
| **Compression.Core**     | net8.0          | Library | Pure RFC 1951 DEFLATE with Zopfli-class optimal parsing. No platform dependencies.                    |
| **Crush.Core**           | net8.0          | Library | Shared CLI utilities: `CrushRunner`, `ICrushOptions`, `OptimizationProgress`, `FileFormatting`.       |
| **Crush.TestUtilities**  | net8.0          | Library | Shared test helpers: `TestBitmapFactory`, `TempFileScope`.                                            |
| **FileFormat.Core**      | net8.0          | Library | Shared data types for file format header field mapping (`HeaderFieldDescriptor`).                      |
| **FileFormat.Png**       | net8.0          | Library | PNG file format reader/writer with CRC32, chunk parsing, Adam7 support.                               |
| **Optimizer.Png**        | net10.0-windows | Library | PNG optimization engine with unsafe pixel access, FrameworkExtensions dithering integration.          |
| **Crush.Png**            | net10.0-windows | Console | CLI wrapper using `CommandLineParser`.                                                                |
| **Optimizer.Gif**        | net8.0-windows  | Library | GIF optimization engine with palette reordering, frame optimization, LZW re-encoding.                 |
| **Crush.Gif**            | net9.0-windows  | Console | CLI for GIF optimization.                                                                             |
| **Optimizer.Tiff**       | net8.0          | Library | TIFF optimization with PackBits, LZW, DEFLATE/Zopfli, tiled encoding.                                 |
| **Crush.Tiff**           | net9.0          | Console | CLI for TIFF optimization.                                                                            |
| **Optimizer.Bmp**        | net8.0          | Library | BMP optimization with RLE4/RLE8 compression, 7 color modes, row order variants.                       |
| **Crush.Bmp**            | net9.0          | Console | CLI for BMP optimization.                                                                             |
| **Optimizer.Tga**        | net8.0          | Library | TGA optimization with pixel-width-aware RLE, 5 color modes, origin variants.                          |
| **Crush.Tga**            | net9.0          | Console | CLI for TGA optimization.                                                                             |
| **Optimizer.Pcx**        | net8.0          | Library | PCX optimization with RLE encoding, plane configurations, palette ordering.                            |
| **Crush.Pcx**            | net9.0          | Console | CLI for PCX optimization.                                                                             |
| **Optimizer.Jpeg**       | net8.0          | Library | JPEG optimization with lossless DCT transfer and lossy re-encoding via LibJpeg.NET.                   |
| **Crush.Jpeg**           | net9.0          | Console | CLI for JPEG optimization.                                                                            |
| **FileFormat.Ico**       | net8.0          | Library | ICO file format reader/writer with BMP DIB and PNG embedding support.                                  |
| **Optimizer.Ico**        | net8.0          | Library | ICO optimization engine with per-entry BMP/PNG format selection.                                       |
| **Crush.Ico**            | net9.0          | Console | CLI for ICO optimization.                                                                             |
| **FileFormat.Cur**       | net8.0          | Library | CUR file format reader/writer with hotspot support. Reuses FileFormat.Ico internals.                   |
| **Optimizer.Cur**        | net8.0          | Library | CUR optimization engine with per-entry BMP/PNG format selection and hotspot preservation.               |
| **Crush.Cur**            | net9.0          | Console | CLI for CUR optimization.                                                                             |
| **FileFormat.Ani**       | net8.0          | Library | ANI animated cursor file format reader/writer using RIFF container and FileFormat.Ico.                  |
| **Optimizer.Ani**        | net8.0          | Library | ANI optimization engine with per-entry BMP/PNG format selection across all animation frames.             |
| **Crush.Ani**            | net9.0          | Console | CLI for ANI optimization.                                                                             |
| **FileFormat.WebP**      | net8.0          | Library | WebP file format reader/writer at RIFF container level (VP8/VP8L/VP8X parsing).                         |
| **Optimizer.WebP**       | net8.0          | Library | WebP optimization engine with metadata stripping and RIFF container rewriting.                           |
| **Crush.WebP**           | net9.0          | Console | CLI for WebP optimization.                                                                              |
| **FileFormat.Riff**      | net8.0          | Library | RIFF container format reader/writer for ANI and WebP support.                                            |
| **FileFormat.Iff**       | net8.0          | Library | IFF (Interchange File Format) big-endian chunk container reader/writer.                                  |
| **FileFormat.Qoi**       | net8.0          | Library | QOI (Quite OK Image) reader/writer with 4 opcodes, 14-byte header.                                      |
| **FileFormat.Farbfeld**  | net8.0          | Library | Farbfeld reader/writer: 16-byte header, raw RGBA16 big-endian pixels.                                   |
| **FileFormat.Wbmp**      | net8.0          | Library | WBMP (Wireless Bitmap) monochrome 1bpp reader/writer with multi-byte integer encoding.                   |
| **FileFormat.Netpbm**    | net8.0          | Library | Netpbm (PBM/PGM/PPM/PAM) reader/writer supporting P1-P7 formats.                                       |
| **FileFormat.Xbm**       | net8.0          | Library | X BitMap reader/writer: C source text format, 1bpp, `#define` dimensions.                               |
| **FileFormat.Xpm**       | net8.0          | Library | X PixMap reader/writer: C source text format, indexed color, XPM3.                                      |
| **FileFormat.MacPaint**  | net8.0          | Library | MacPaint reader/writer: 576x720 monochrome, PackBits compressed, 512-byte brush patterns.               |
| **FileFormat.ZxSpectrum** | net8.0         | Library | ZX Spectrum screen dump reader/writer: 256x192, 1bpp bitmap + 8x8 attribute blocks.                    |
| **FileFormat.Koala**     | net8.0          | Library | Commodore 64 Koala Painter reader/writer: 160x200 multicolor, 16 C64 colors.                           |
| **FileFormat.Degas**     | net8.0          | Library | Atari ST DEGAS/DEGAS Elite reader/writer: planar, optional PackBits compression.                        |
| **FileFormat.Neochrome** | net8.0          | Library | Atari ST NEOchrome reader/writer: 128-byte header + 32000 bytes planar.                                |
| **FileFormat.GemImg**    | net8.0          | Library | GEM Raster Image reader/writer: scan-line encoding with vertical RLE and pattern replication.            |
| **FileFormat.AmstradCpc** | net8.0         | Library | Amstrad CPC screen memory dump reader/writer with CPC memory interleave and pixel packing for Mode 0/1/2. |
| **FileFormat.Pfm**       | net8.0          | Library | Portable Float Map reader/writer: text header, float32 pixels, endianness from scale sign.              |
| **FileFormat.Sgi**       | net8.0          | Library | Silicon Graphics Image reader/writer: 512-byte header, channel-plane RLE, big-endian.                   |
| **FileFormat.SunRaster** | net8.0          | Library | Sun Raster reader/writer: 32-byte header, escape-based RLE (0x80), big-endian.                         |
| **FileFormat.Hdr**       | net8.0          | Library | Radiance HDR/RGBE reader/writer: text header, RGBE encoding, scanline RLE.                              |
| **FileFormat.UtahRle**   | net8.0          | Library | Utah Raster Toolkit reader/writer: multi-channel scanline operations.                                   |
| **FileFormat.DrHalo**    | net8.0          | Library | Dr. Halo CUT reader/writer: 8-bit indexed, per-scanline RLE, separate .PAL palette.                    |
| **FileFormat.Ilbm**      | net8.0          | Library | IFF ILBM reader/writer: planar bitmap, ByteRun1 (PackBits), CAMG chunk, HAM6/HAM8 and EHB modes.       |
| **FileFormat.Fli**       | net8.0          | Library | Autodesk FLI/FLC animation reader/writer: frame-differential encoding.                                  |
| **FileFormat.Cineon**    | net8.0          | Library | Kodak Cineon reader/writer: 1024-byte header, 10-bit log film scanning.                                |
| **FileFormat.Dds**       | net8.0          | Library | DirectDraw Surface reader/writer: 128-byte header, optional DX10 header, GPU textures.                  |
| **FileFormat.Vtf**       | net8.0          | Library | Valve Texture Format reader/writer: VTF 7.x, mipmaps, BCn + custom formats.                            |
| **FileFormat.Ktx**       | net8.0          | Library | KTX1 + KTX2 GPU texture container reader/writer for OpenGL/Vulkan.                                     |
| **FileFormat.Exr**       | net8.0          | Library | OpenEXR reader/writer: single-part scanline, None compression, Half/Float/UInt.                         |
| **FileFormat.Dpx**       | net8.0          | Library | Digital Picture Exchange reader/writer: 2048-byte header, 10-bit packed pixels.                         |
| **FileFormat.Fits**      | net8.0          | Library | FITS (astronomy) reader/writer: 80-char card headers, multi-dimensional arrays.                         |
| **FileFormat.Ccitt**     | net8.0          | Library | CCITT Group 3/4 fax compression reader/writer: Huffman-coded 1bpp bi-level.                             |
| **FileFormat.BbcMicro**  | net8.0          | Library | BBC Micro screen dump reader/writer: character-block layout, Mode 0/1/2/4/5.                            |
| **FileFormat.C64Multi**  | net8.0          | Library | C64 multiformat art reader/writer: Art Studio Hires/Multicolor.                                        |
| **FileFormat.Psd**       | net8.0          | Library | Adobe Photoshop reader/writer: flat composite image, RLE/Raw, 8 color modes.                            |
| **FileFormat.Hrz**       | net8.0          | Library | Slow-Scan Television reader/writer: 256x240 fixed, raw RGB, no header, 184,320 bytes exact.             |
| **FileFormat.Cmu**       | net8.0          | Library | CMU Window Manager Bitmap reader/writer: 8-byte header, 1bpp packed MSB-first, big-endian.              |
| **FileFormat.Mtv**       | net8.0          | Library | MTV Ray Tracer reader/writer: ASCII "width height\n" header, raw RGB pixel data.                        |
| **FileFormat.Qrt**       | net8.0          | Library | QRT Ray Tracer reader/writer: 10-byte header, raw RGB pixel data.                                       |
| **FileFormat.Msp**       | net8.0          | Library | Microsoft Paint v1/v2 reader/writer: 32-byte header, 1bpp monochrome, V2 RLE compression.              |
| **FileFormat.Dcx**       | net8.0          | Library | Multi-page PCX container reader/writer: 0x3ADE68B1 magic, up to 1023 page offsets. References FileFormat.Pcx. |
| **FileFormat.Astc**      | net8.0          | Library | Adaptive Scalable Texture Compression reader/writer: 16-byte header, 0x5CA1AB13 magic, raw ASTC blocks. |
| **FileFormat.Pkm**       | net8.0          | Library | Ericsson Texture Container reader/writer: 16-byte header, "PKM " magic, ETC1/ETC2 blocks.              |
| **FileFormat.Tim**       | net8.0          | Library | PlayStation 1 Texture reader/writer: 8-byte header, 0x10 magic, 4/8/16/24-bit modes, optional CLUT.    |
| **FileFormat.Tim2**      | net8.0          | Library | PlayStation 2/PSP Texture reader/writer: "TIM2" magic, 16-byte file header, 48-byte picture headers.   |
| **FileFormat.Wal**       | net8.0          | Library | Quake 2 Texture reader/writer: 100-byte header, 8-bit indexed, 4 mipmap levels, no embedded palette.   |
| **FileFormat.Pvr**       | net8.0          | Library | PowerVR Texture v3 reader/writer: 52-byte header, GPU texture container.                                |
| **FileFormat.Wpg**       | net8.0          | Library | WordPerfect Graphics reader/writer: 16-byte header, record-based, RLE bitmap records.                  |
| **FileFormat.Bsave**     | net8.0          | Library | IBM PC BSAVE Graphics reader/writer: 7-byte header, 0xFD magic, screen memory dump with mode detection. |
| **FileFormat.Clp**       | net8.0          | Library | Windows Clipboard reader/writer: 4-byte header, format directory, embedded DIB data.                   |
| **FileFormat.Spectrum512** | net8.0        | Library | Atari ST 512-color reader/writer: 51,104 bytes, 320x199, 48 palettes per scanline.                     |
| **FileFormat.Tiny**      | net8.0          | Library | Atari ST Compressed DEGAS reader/writer: resolution byte + palette + delta+word-level RLE.              |
| **FileFormat.Sixel**     | net8.0          | Library | DEC Terminal Graphics reader/writer: text-based encoding, ESC P sixel-data ESC \, 6-pixel bands.        |
| **FileFormat.Wad**       | net8.0          | Library | Doom WAD container reader/writer: "IWAD"/"PWAD" magic, 12-byte header, named lumps.                    |
| **FileFormat.Wad3**      | net8.0          | Library | Half-Life WAD3 texture container reader/writer: "WAD3" magic, MipTex with embedded palette.            |
| **FileFormat.Apng**      | net8.0          | Library | Animated PNG reader/writer: extends PNG with acTL/fcTL/fdAT chunks. References FileFormat.Png + Compression.Core. |
| **FileFormat.Mng**       | net8.0          | Library | Multiple Network Graphics VLC subset reader/writer: MNG signature, MHDR chunk, embedded PNG frames.    |
| **FileFormat.Xcf**       | net8.0          | Library | GIMP Native (flat composite) reader/writer: "gimp xcf" magic, 64x64 tiles, per-channel RLE/zlib.      |
| **FileFormat.Pict**      | net8.0          | Library | Apple QuickDraw (raster subset) reader/writer: 512-byte preamble, PICT2 opcodes, PackBits.             |
| **FileFormat.Dicom**     | net8.0          | Library | Medical Imaging (basic subset) reader/writer: 128-byte preamble + "DICM", tag-length-value, Explicit VR LE. |
| **Compression.Tests**    | net8.0          | NUnit 4 | 51 tests for `ZopfliDeflater`.                                                                        |
| **Optimizer.Png.Tests**  | net10.0-windows | NUnit 4 | 184 tests: filters, combos, E2E, ditherers, regression, performance.                                  |
| **Optimizer.Gif.Tests**  | net8.0-windows  | NUnit 4 | 71 tests: reader, LZW, palette, frames, dedup, E2E.                                                   |
| **Optimizer.Tiff.Tests** | net8.0          | NUnit 4 | 36 tests: PackBits, optimizer, E2E with LibTiff readback.                                             |
| **Optimizer.Bmp.Tests**  | net8.0          | NUnit 4 | 24 tests: RLE round-trip, optimizer, E2E with readback.                                               |
| **Optimizer.Tga.Tests**  | net8.0          | NUnit 4 | 21 tests: TGA RLE round-trip, optimizer, E2E with readback.                                           |
| **Optimizer.Pcx.Tests**  | net8.0          | NUnit 4 | 21 tests: PCX RLE round-trip, optimizer, E2E with readback.                                           |
| **Optimizer.Jpeg.Tests** | net8.0          | NUnit 4 | 17 tests: assembler, optimizer, E2E with lossless/lossy modes.                                        |
| **Optimizer.Ico.Tests**  | net8.0          | NUnit 4 | 9 tests: input validation, E2E with IcoReader readback, cancellation, progress.                       |
| **FileFormat.Cur.Tests** | net8.0          | NUnit 4 | 11 tests: reader validation, writer header/hotspot, round-trip with hotspot preservation.              |
| **Optimizer.Cur.Tests**  | net8.0          | NUnit 4 | 8 tests: input validation, E2E with CurReader readback, hotspot preservation, cancellation.            |
| **FileFormat.Ani.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip with rates/sequence, data type tests.           |
| **Optimizer.Ani.Tests**  | net8.0          | NUnit 4 | 7 tests: input validation, E2E with AniReader readback, cancellation.                                  |
| **FileFormat.WebP.Tests** | net8.0         | NUnit 4 | 21 tests: reader validation, writer output, round-trip, data type tests.                                |
| **Optimizer.WebP.Tests** | net8.0          | NUnit 4 | 9 tests: input validation, E2E metadata stripping, cancellation, progress, lossy/lossless.              |
| **FileFormat.Bmp.Tests** | net8.0          | NUnit 4 | 28 tests: reader validation, writer structure, round-trip (7 color modes), RLE compressor, data types.  |
| **FileFormat.Tga.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer structure, round-trip (5 modes + RLE), RLE compressor, data types.  |
| **FileFormat.Pcx.Tests** | net8.0          | NUnit 4 | 22 tests: reader validation, writer structure, round-trip (4 modes), RLE compressor, data types.        |
| **FileFormat.Jpeg.Tests** | net8.0         | NUnit 4 | 17 tests: reader validation, writer lossy/lossless, round-trip, data types.                             |
| **FileFormat.Tiff.Tests** | net8.0         | NUnit 4 | 23 tests: reader validation, writer structure, round-trip (6 modes), PackBits compressor, data types.   |
| **FileFormat.Png.Tests** | net8.0          | NUnit 4 | 32 tests: reader validation, writer structure, round-trip (6 modes), chunk reader, Adam7, data types.   |
| **FileFormat.Ico.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer header/dimensions, round-trip (PNG/BMP/256x256), data types.        |
| **FileFormat.Riff.Tests** | net8.0         | NUnit 4 | 22 tests: FourCC, reader validation, writer output, round-trip with nested LISTs.                      |
| **FileFormat.Iff.Tests** | net8.0          | NUnit 4 | 22 tests: reader validation, writer output, round-trip, chunk header tests.                            |
| **FileFormat.Qoi.Tests** | net8.0          | NUnit 4 | 43 tests: reader validation, writer output, codec round-trip, data types, header tests.                |
| **FileFormat.Farbfeld.Tests** | net8.0     | NUnit 4 | 26 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Wbmp.Tests** | net8.0         | NUnit 4 | 33 tests: reader validation, writer structure, multi-byte int encode/decode, round-trip.               |
| **FileFormat.Netpbm.Tests** | net8.0       | NUnit 4 | 45 tests: reader validation per format, writer output, header parser, round-trip P1-P7.                |
| **FileFormat.Xbm.Tests** | net8.0          | NUnit 4 | 37 tests: reader validation, writer output, text parser, round-trip.                                   |
| **FileFormat.Xpm.Tests** | net8.0          | NUnit 4 | 30 tests: reader validation, writer output, text parser, round-trip, data types.                       |
| **FileFormat.MacPaint.Tests** | net8.0     | NUnit 4 | 29 tests: reader validation, writer output, PackBits, round-trip, header tests.                        |
| **FileFormat.ZxSpectrum.Tests** | net8.0   | NUnit 4 | 24 tests: reader validation, writer output, interleave verification, round-trip.                       |
| **FileFormat.Koala.Tests** | net8.0        | NUnit 4 | 24 tests: reader validation, writer output, round-trip.                                                |
| **FileFormat.Degas.Tests** | net8.0        | NUnit 4 | 22 tests: reader validation, writer output, planar conversion, round-trip, header tests.               |
| **FileFormat.Neochrome.Tests** | net8.0    | NUnit 4 | 25 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.GemImg.Tests** | net8.0       | NUnit 4 | 21 tests: reader validation, writer output, compression, round-trip, header tests.                     |
| **FileFormat.AmstradCpc.Tests** | net8.0   | NUnit 4 | 29 tests: reader validation, writer output, pixel packer round-trip, round-trip, data types.           |
| **FileFormat.Pfm.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, header parser, round-trip, data types.                     |
| **FileFormat.Sgi.Tests** | net8.0          | NUnit 4 | 29 tests: reader validation, writer output, RLE compressor, round-trip, header tests.                  |
| **FileFormat.SunRaster.Tests** | net8.0    | NUnit 4 | 37 tests: reader validation, writer output, RLE compressor, round-trip, header tests.                  |
| **FileFormat.Hdr.Tests** | net8.0          | NUnit 4 | 40 tests: reader validation, writer output, RGBE codec, header parser, round-trip.                     |
| **FileFormat.UtahRle.Tests** | net8.0      | NUnit 4 | 29 tests: reader validation, writer output, decoder/encoder, round-trip, header tests.                 |
| **FileFormat.DrHalo.Tests** | net8.0       | NUnit 4 | 30 tests: reader validation, writer output, RLE, round-trip, header tests.                             |
| **FileFormat.Ilbm.Tests** | net8.0         | NUnit 4 | 53 tests: reader validation, writer output, ByteRun1, planar converter, round-trip, CAMG/HAM/EHB, header tests. |
| **FileFormat.Fli.Tests** | net8.0          | NUnit 4 | 37 tests: reader validation, writer output, delta decoder, round-trip, header tests.                   |
| **FileFormat.Cineon.Tests** | net8.0       | NUnit 4 | 23 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Dds.Tests** | net8.0          | NUnit 4 | 50 tests: reader validation, writer output, block info, round-trip, header/pixel format/DX10 tests.    |
| **FileFormat.Vtf.Tests** | net8.0          | NUnit 4 | 23 tests: reader validation, writer output, round-trip, data types, header tests.                      |
| **FileFormat.Ktx.Tests** | net8.0          | NUnit 4 | 33 tests: reader validation, writer output, round-trip, KTX1 + KTX2 header tests.                     |
| **FileFormat.Exr.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, round-trip, magic header tests.                            |
| **FileFormat.Dpx.Tests** | net8.0          | NUnit 4 | 32 tests: reader validation, writer output, round-trip, BE/LE endianness, header tests.                |
| **FileFormat.Fits.Tests** | net8.0         | NUnit 4 | 28 tests: reader validation, writer output, header parser, round-trip, data types.                     |
| **FileFormat.Ccitt.Tests** | net8.0        | NUnit 4 | 46 tests: reader validation, writer output, G3/G4 codecs, Huffman tables, round-trip.                  |
| **FileFormat.BbcMicro.Tests** | net8.0     | NUnit 4 | 34 tests: reader validation, writer output, layout converter, round-trip, data types.                  |
| **FileFormat.C64Multi.Tests** | net8.0     | NUnit 4 | 26 tests: reader validation, writer output, round-trip (hires + multicolor), data types.               |
| **FileFormat.Psd.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, round-trip, header tests, data types.                      |
| **FileFormat.Hrz.Tests** | net8.0          | NUnit 4 | 13 tests: reader validation, writer output, round-trip.                                                |
| **FileFormat.Cmu.Tests** | net8.0          | NUnit 4 | 19 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Mtv.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Qrt.Tests** | net8.0          | NUnit 4 | 17 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Msp.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, RLE compressor, round-trip, header tests.                  |
| **FileFormat.Dcx.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip, multi-page PCX container.                      |
| **FileFormat.Astc.Tests** | net8.0         | NUnit 4 | 21 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Pkm.Tests** | net8.0          | NUnit 4 | 19 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Tim.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, round-trip, CLUT, header tests.                            |
| **FileFormat.Tim2.Tests** | net8.0         | NUnit 4 | 22 tests: reader validation, writer output, round-trip, multi-picture, header tests.                   |
| **FileFormat.Wal.Tests** | net8.0          | NUnit 4 | 21 tests: reader validation, writer output, round-trip, mipmap, header tests.                          |
| **FileFormat.Pvr.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Wpg.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer output, RLE, round-trip, header tests.                             |
| **FileFormat.Bsave.Tests** | net8.0        | NUnit 4 | 25 tests: reader validation, writer output, round-trip, mode detection, header tests.                  |
| **FileFormat.Clp.Tests** | net8.0          | NUnit 4 | 18 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Spectrum512.Tests** | net8.0  | NUnit 4 | 13 tests: reader validation, writer output, round-trip.                                                |
| **FileFormat.Tiny.Tests** | net8.0         | NUnit 4 | 20 tests: reader validation, writer output, round-trip, compression, header tests.                     |
| **FileFormat.Sixel.Tests** | net8.0        | NUnit 4 | 22 tests: reader validation, writer output, round-trip, encoding tests.                                |
| **FileFormat.Wad.Tests** | net8.0          | NUnit 4 | 29 tests: reader validation, writer output, round-trip, lump management, header tests.                 |
| **FileFormat.Wad3.Tests** | net8.0         | NUnit 4 | 26 tests: reader validation, writer output, round-trip, MipTex, palette, header tests.                 |
| **FileFormat.Apng.Tests** | net8.0         | NUnit 4 | 31 tests: reader validation, writer output, round-trip, acTL/fcTL/fdAT chunks, animation.             |
| **FileFormat.Mng.Tests** | net8.0          | NUnit 4 | 23 tests: reader validation, writer output, round-trip, MHDR chunk, embedded PNG frames.               |
| **FileFormat.Xcf.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, round-trip, tile encoding, RLE/zlib, header tests.         |
| **FileFormat.Pict.Tests** | net8.0         | NUnit 4 | 16 tests: reader validation, writer output, round-trip, PackBits, header tests.                        |
| **FileFormat.Dicom.Tests** | net8.0        | NUnit 4 | 23 tests: reader validation, writer output, round-trip, tag parsing, header tests.                     |
| **GifFileFormat**        | net8.0-windows  | Library | GIF reader/writer with LZW codec (in [AnythingToGif](https://github.com/Hawkynt/AnythingToGif) repo). |
| **FileFormat.Bmp**       | net8.0          | Library | BMP file format reader/writer with RLE support.                                                        |
| **FileFormat.Tga**       | net8.0          | Library | TGA file format reader/writer with RLE and TGA 2.0 footer support.                                     |
| **FileFormat.Pcx**       | net8.0          | Library | PCX file format reader/writer with RLE encoding.                                                        |
| **FileFormat.Jpeg**      | net8.0          | Library | JPEG file format reader/writer (wraps BitMiracle.LibJpeg.NET).                                          |
| **FileFormat.Tiff**      | net8.0          | Library | TIFF file format reader/writer (wraps BitMiracle.LibTiff.NET + Compression.Core).                       |

### Key Types

| Type                  | Project          | Purpose                                                                                                                                                                                                                                                            |
| --------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ZopfliDeflater`      | Compression.Core | Zopfli-class DEFLATE encoder: direct lookup tables, distance-aware lazy matching, adaptive hash chain depth, multi-length DP optimal parsing, iterative refinement with convergence detection, Huffman-cost block splitting, cached RLE, ArrayPool-backed reparse. |
| `PngOptimizer`        | Optimizer.Png    | Main PNG engine. Pixel conversion, palette quantization, FrameworkExtensions dithering, tRNS generation, PNG assembly.                                                                                                                                             |
| `FilterTools`         | Optimizer.Png    | SIMD-accelerated PNG row filters (None, Sub, Up, Average, Paeth).                                                                                                                                                                                                  |
| `PngFilterOptimizer`  | Optimizer.Png    | Applies filter strategies across scanlines, including BruteForce with lookahead compression.                                                                                                                                                                       |
| `PngFilterSelector`   | Optimizer.Png    | Deflate-aware per-scanline filter selection with byte-pair and run bonuses.                                                                                                                                                                                        |
| `MedianCutQuantizer`  | Optimizer.Png    | Built-in median-cut color quantization for lossy palette reduction.                                                                                                                                                                                                |
| `PngPaletteReorderer` | Optimizer.Png    | Hilbert, spatial locality, and deflate-optimized palette orderings.                                                                                                                                                                                                |
| `ImagePartitioner`    | Optimizer.Png    | Content-aware row partitioning for `PartitionOptimized` strategy.                                                                                                                                                                                                  |
| `PngChunkReader`      | FileFormat.Png   | PNG parser for chunk reading, ancillary chunk preservation.                                                                                                                                                                                                        |
| `Adam7`               | FileFormat.Png   | Adam7 interlace pass definitions.                                                                                                                                                                                                                                  |
| `GifOptimizer`        | Optimizer.Gif    | Main GIF engine. Parses input, generates combos, tests in parallel.                                                                                                                                                                                                |
| `PaletteReorderer`    | Optimizer.Gif    | 7 palette reorder strategies including CompressionOptimized.                                                                                                                                                                                                       |
| `GifFrameOptimizer`   | Optimizer.Gif    | Disposal optimization, margin trimming, palette-aware frame deduplication.                                                                                                                                                                                         |
| `GifFrameDifferencer` | Optimizer.Gif    | Pixel differencing between consecutive frames.                                                                                                                                                                                                                     |
| `LzwCompressor`       | Optimizer.Gif    | Standalone LZW encoder with deferred clear code support.                                                                                                                                                                                                           |
| `GifAssembler`        | Optimizer.Gif    | GIF byte stream assembly from optimized components.                                                                                                                                                                                                                |
| `TiffOptimizer`       | Optimizer.Tiff   | Main TIFF engine. Pixel extraction, combo generation, parallel testing.                                                                                                                                                                                            |
| `BmpOptimizer`        | Optimizer.Bmp    | Main BMP engine. Pixel extraction, color mode detection, RLE pruning, palette building with frequency sort.                                                                                                                                                        |
| `TgaOptimizer`        | Optimizer.Tga    | Main TGA engine. Alpha detection, BGRA/BGR/Grayscale/Indexed conversion, combo pruning.                                                                                                                                                                           |
| `PcxOptimizer`        | Optimizer.Pcx    | Main PCX engine. Color mode detection, plane config pruning, palette ordering.                                                                                                                                                                                     |
| `JpegOptimizer`       | Optimizer.Jpeg   | Main JPEG engine. Lossless DCT coefficient transfer and lossy pixel re-encoding via LibJpeg.NET.                                                                                                                                                                   |
| `IcoOptimizer`        | Optimizer.Ico    | Main ICO engine. Per-entry BMP/PNG format selection, 2^n combo generation (capped at 256), parallel testing.                                                                                                                                                       |
| `Frame`               | GifFileFormat    | Unified GIF frame carrying indexed pixels, palette, delay, disposal, transparency. `FromBitmap` factory for Bitmap-to-indexed conversion.                                                                                                                          |
| `Reader`              | GifFileFormat    | Static GIF parser with LZW decoder and interlace deinterleaving.                                                                                                                                                                                                   |
| `PngReader`           | FileFormat.Png   | Static PNG parser: signature validation, IDAT decompression, de-filtering, Adam7 de-interlace.                                                                                                                                                                     |
| `PngWriter`           | FileFormat.Png   | PNG byte stream assembly: IHDR, PLTE, tRNS, IDAT, ancillary chunks, CRC32, IEND.                                                                                                                                                                                   |
| `PngFile`             | FileFormat.Png   | PNG data model: Width, Height, BitDepth, ColorType, PixelData, Palette, Transparency.                                                                                                                                                                               |
| `BmpReader`           | FileFormat.Bmp   | Static BMP parser: BITMAPFILEHEADER, BITMAPINFOHEADER, RLE decompression, palette extraction.                                                                                                                                                                       |
| `BmpWriter`           | FileFormat.Bmp   | BMP byte stream assembly from BmpFile data model.                                                                                                                                                                                                                    |
| `TgaReader`           | FileFormat.Tga   | Static TGA parser: 18-byte header, color map, pixel data, RLE decompression, TGA 2.0 footer.                                                                                                                                                                       |
| `TgaWriter`           | FileFormat.Tga   | TGA byte stream assembly from TgaFile data model.                                                                                                                                                                                                                    |
| `PcxReader`           | FileFormat.Pcx   | Static PCX parser: 128-byte header, RLE scanlines, VGA palette.                                                                                                                                                                                                      |
| `PcxWriter`           | FileFormat.Pcx   | PCX byte stream assembly from PcxFile data model.                                                                                                                                                                                                                    |
| `JpegReader`          | FileFormat.Jpeg  | Static JPEG parser via LibJpeg.NET wrapper.                                                                                                                                                                                                                          |
| `JpegWriter`          | FileFormat.Jpeg  | JPEG encoding: lossless transcode and lossy encode via LibJpeg.NET.                                                                                                                                                                                                  |
| `TiffReader`          | FileFormat.Tiff  | Static TIFF parser via LibTiff.NET wrapper.                                                                                                                                                                                                                          |
| `TiffWriter`          | FileFormat.Tiff  | TIFF byte stream assembly with custom raw strip/tile writing for Zopfli integration.                                                                                                                                                                                |
| `IcoReader`           | FileFormat.Ico   | Static ICO parser: header, directory entries, auto-detects BMP DIB vs PNG via signature sniffing.                                                                                                                                                                   |
| `IcoWriter`           | FileFormat.Ico   | ICO byte stream assembly: header, directory entries, image data.                                                                                                                                                                                                     |
| `CurReader`           | FileFormat.Cur   | Static CUR parser: reuses IcoReader internals with type=2 validation, extracts hotspot coordinates from directory entries.                                                                                                                                           |
| `CurWriter`           | FileFormat.Cur   | CUR byte stream assembly: reuses IcoWriter internals with type=2 and hotspot field override.                                                                                                                                                                         |
| `CurOptimizer`        | Optimizer.Cur    | Main CUR engine. Per-entry BMP/PNG format selection, 2^n combo generation (capped at 256), hotspot preservation, parallel testing.                                                                                                                                   |
| `AniReader`           | FileFormat.Ani   | Static ANI parser: RIFF ACON validation, anih header parsing, rate/sequence chunk extraction, LIST fram with ICO frame parsing.                                                                                                                                      |
| `AniWriter`           | FileFormat.Ani   | ANI byte stream assembly: RIFF ACON container with anih, optional rate/seq chunks, LIST fram with ICO frames.                                                                                                                                                        |
| `AniOptimizer`        | Optimizer.Ani    | Main ANI engine. Per-entry BMP/PNG format selection across all frames, 2^n combo generation (capped at 256), structure preservation, parallel testing.                                                                                                                |
| `WebPReader`          | FileFormat.WebP  | Static WebP parser: RIFF WEBP validation, VP8/VP8L/VP8X chunk extraction, dimension and feature flag parsing.                                                                                                                                                        |
| `WebPWriter`          | FileFormat.WebP  | WebP byte stream assembly: simple (VP8/VP8L only) or extended (VP8X + image + metadata chunks).                                                                                                                                                                      |
| `WebPOptimizer`       | Optimizer.WebP   | Main WebP engine. Metadata stripping, RIFF container rewriting, parallel combo testing.                                                                                                                                                                              |
| `RiffReader`          | FileFormat.Riff  | Static RIFF parser: signature validation, recursive LIST/chunk parsing, word-aligned offsets.                                                                                                                                                                         |
| `RiffWriter`          | FileFormat.Riff  | RIFF byte stream assembly with automatic size patching and word alignment.                                                                                                                                                                                            |
| `WbmpReader`          | FileFormat.Wbmp  | Static WBMP parser: type byte validation, multi-byte integer dimension decoding, 1bpp pixel data extraction.                                                                                                                                                         |
| `WbmpWriter`          | FileFormat.Wbmp  | WBMP byte stream assembly: type/fixed header bytes, multi-byte integer dimensions, raw 1bpp pixel data.                                                                                                                                                               |
| `AmstradCpcReader`    | FileFormat.AmstradCpc | Static CPC screen dump parser: 16384-byte validation, CPC memory layout deinterleaving, mode-parameterized width.                                                                                                                                                |
| `AmstradCpcWriter`    | FileFormat.AmstradCpc | CPC screen dump assembly: linear-to-CPC memory interleaving, 16384-byte output.                                                                                                                                                                                  |
| `AmstradCpcPixelPacker` | FileFormat.AmstradCpc | Internal pixel index packing/unpacking for Mode 0 (2px/byte), Mode 1 (4px/byte), Mode 2 (8px/byte) bit-interleaved formats.                                                                                                                                    |
| `C64MultiReader`      | FileFormat.C64Multi | Static C64 multi parser: format detection from file size, load address, bitmap/screen/color data, background color.                                                                                                                                                |
| `C64MultiWriter`      | FileFormat.C64Multi | C64 multi byte stream assembly: load address, data sections, padding per format variant.                                                                                                                                                                            |
| `CrushRunner`         | Crush.Core       | Shared CLI runner: input/output validation, cancellation, progress reporting, stopwatch, savings display.                                                                                                                                                            |
| `OptimizationProgress` | Crush.Core      | Shared progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar`, `Phase`.                                                                                                                                                                                 |

## Build / Test / Run

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Run all tests
dotnet test Compression.Tests/Compression.Tests.csproj
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj
dotnet test Optimizer.Gif.Tests/Optimizer.Gif.Tests.csproj
dotnet test Optimizer.Tiff.Tests/Optimizer.Tiff.Tests.csproj
dotnet test Optimizer.Bmp.Tests/Optimizer.Bmp.Tests.csproj
dotnet test Optimizer.Tga.Tests/Optimizer.Tga.Tests.csproj
dotnet test Optimizer.Pcx.Tests/Optimizer.Pcx.Tests.csproj
dotnet test Optimizer.Jpeg.Tests/Optimizer.Jpeg.Tests.csproj
dotnet test Optimizer.Ico.Tests/Optimizer.Ico.Tests.csproj
dotnet test FileFormat.Ico.Tests/FileFormat.Ico.Tests.csproj
dotnet test FileFormat.Cur.Tests/FileFormat.Cur.Tests.csproj
dotnet test Optimizer.Cur.Tests/Optimizer.Cur.Tests.csproj
dotnet test FileFormat.Ani.Tests/FileFormat.Ani.Tests.csproj
dotnet test Optimizer.Ani.Tests/Optimizer.Ani.Tests.csproj
dotnet test FileFormat.WebP.Tests/FileFormat.WebP.Tests.csproj
dotnet test Optimizer.WebP.Tests/Optimizer.WebP.Tests.csproj
dotnet test FileFormat.Riff.Tests/FileFormat.Riff.Tests.csproj
dotnet test FileFormat.Bmp.Tests/FileFormat.Bmp.Tests.csproj
dotnet test FileFormat.Tga.Tests/FileFormat.Tga.Tests.csproj
dotnet test FileFormat.Pcx.Tests/FileFormat.Pcx.Tests.csproj
dotnet test FileFormat.Jpeg.Tests/FileFormat.Jpeg.Tests.csproj
dotnet test FileFormat.Tiff.Tests/FileFormat.Tiff.Tests.csproj
dotnet test FileFormat.Png.Tests/FileFormat.Png.Tests.csproj
dotnet test FileFormat.Wbmp.Tests/FileFormat.Wbmp.Tests.csproj
dotnet test FileFormat.Qoi.Tests/FileFormat.Qoi.Tests.csproj
dotnet test FileFormat.Farbfeld.Tests/FileFormat.Farbfeld.Tests.csproj
dotnet test FileFormat.Netpbm.Tests/FileFormat.Netpbm.Tests.csproj
dotnet test FileFormat.Xbm.Tests/FileFormat.Xbm.Tests.csproj
dotnet test FileFormat.Xpm.Tests/FileFormat.Xpm.Tests.csproj
dotnet test FileFormat.MacPaint.Tests/FileFormat.MacPaint.Tests.csproj
dotnet test FileFormat.ZxSpectrum.Tests/FileFormat.ZxSpectrum.Tests.csproj
dotnet test FileFormat.Koala.Tests/FileFormat.Koala.Tests.csproj
dotnet test FileFormat.Degas.Tests/FileFormat.Degas.Tests.csproj
dotnet test FileFormat.Neochrome.Tests/FileFormat.Neochrome.Tests.csproj
dotnet test FileFormat.GemImg.Tests/FileFormat.GemImg.Tests.csproj
dotnet test FileFormat.AmstradCpc.Tests/FileFormat.AmstradCpc.Tests.csproj
dotnet test FileFormat.Pfm.Tests/FileFormat.Pfm.Tests.csproj
dotnet test FileFormat.Sgi.Tests/FileFormat.Sgi.Tests.csproj
dotnet test FileFormat.SunRaster.Tests/FileFormat.SunRaster.Tests.csproj
dotnet test FileFormat.Hdr.Tests/FileFormat.Hdr.Tests.csproj
dotnet test FileFormat.UtahRle.Tests/FileFormat.UtahRle.Tests.csproj
dotnet test FileFormat.DrHalo.Tests/FileFormat.DrHalo.Tests.csproj
dotnet test FileFormat.Iff.Tests/FileFormat.Iff.Tests.csproj
dotnet test FileFormat.Ilbm.Tests/FileFormat.Ilbm.Tests.csproj
dotnet test FileFormat.Fli.Tests/FileFormat.Fli.Tests.csproj
dotnet test FileFormat.Cineon.Tests/FileFormat.Cineon.Tests.csproj
dotnet test FileFormat.Dds.Tests/FileFormat.Dds.Tests.csproj
dotnet test FileFormat.Vtf.Tests/FileFormat.Vtf.Tests.csproj
dotnet test FileFormat.Ktx.Tests/FileFormat.Ktx.Tests.csproj
dotnet test FileFormat.Exr.Tests/FileFormat.Exr.Tests.csproj
dotnet test FileFormat.Dpx.Tests/FileFormat.Dpx.Tests.csproj
dotnet test FileFormat.Fits.Tests/FileFormat.Fits.Tests.csproj
dotnet test FileFormat.Ccitt.Tests/FileFormat.Ccitt.Tests.csproj
dotnet test FileFormat.BbcMicro.Tests/FileFormat.BbcMicro.Tests.csproj
dotnet test FileFormat.C64Multi.Tests/FileFormat.C64Multi.Tests.csproj
dotnet test FileFormat.Psd.Tests/FileFormat.Psd.Tests.csproj
dotnet test FileFormat.Hrz.Tests/FileFormat.Hrz.Tests.csproj
dotnet test FileFormat.Cmu.Tests/FileFormat.Cmu.Tests.csproj
dotnet test FileFormat.Mtv.Tests/FileFormat.Mtv.Tests.csproj
dotnet test FileFormat.Qrt.Tests/FileFormat.Qrt.Tests.csproj
dotnet test FileFormat.Msp.Tests/FileFormat.Msp.Tests.csproj
dotnet test FileFormat.Dcx.Tests/FileFormat.Dcx.Tests.csproj
dotnet test FileFormat.Astc.Tests/FileFormat.Astc.Tests.csproj
dotnet test FileFormat.Pkm.Tests/FileFormat.Pkm.Tests.csproj
dotnet test FileFormat.Tim.Tests/FileFormat.Tim.Tests.csproj
dotnet test FileFormat.Tim2.Tests/FileFormat.Tim2.Tests.csproj
dotnet test FileFormat.Wal.Tests/FileFormat.Wal.Tests.csproj
dotnet test FileFormat.Pvr.Tests/FileFormat.Pvr.Tests.csproj
dotnet test FileFormat.Wpg.Tests/FileFormat.Wpg.Tests.csproj
dotnet test FileFormat.Bsave.Tests/FileFormat.Bsave.Tests.csproj
dotnet test FileFormat.Clp.Tests/FileFormat.Clp.Tests.csproj
dotnet test FileFormat.Spectrum512.Tests/FileFormat.Spectrum512.Tests.csproj
dotnet test FileFormat.Tiny.Tests/FileFormat.Tiny.Tests.csproj
dotnet test FileFormat.Sixel.Tests/FileFormat.Sixel.Tests.csproj
dotnet test FileFormat.Wad.Tests/FileFormat.Wad.Tests.csproj
dotnet test FileFormat.Wad3.Tests/FileFormat.Wad3.Tests.csproj
dotnet test FileFormat.Apng.Tests/FileFormat.Apng.Tests.csproj
dotnet test FileFormat.Mng.Tests/FileFormat.Mng.Tests.csproj
dotnet test FileFormat.Xcf.Tests/FileFormat.Xcf.Tests.csproj
dotnet test FileFormat.Pict.Tests/FileFormat.Pict.Tests.csproj
dotnet test FileFormat.Dicom.Tests/FileFormat.Dicom.Tests.csproj

# Run specific tool
dotnet run --project Crush.Png -- -i <input.png> -o <output.png>
dotnet run --project Crush.Gif -- -i <input.gif> -o <output.gif>
dotnet run --project Crush.Tiff -- -i <input.tiff> -o <output.tiff>
dotnet run --project Crush.Bmp -- -i <input.bmp> -o <output.bmp>
dotnet run --project Crush.Tga -- -i <input.tga> -o <output.tga>
dotnet run --project Crush.Pcx -- -i <input.pcx> -o <output.pcx>
dotnet run --project Crush.Jpeg -- -i <input.jpg> -o <output.jpg>
dotnet run --project Crush.Ico -- -i <input.ico> -o <output.ico>
dotnet run --project Crush.Cur -- -i <input.cur> -o <output.cur>
dotnet run --project Crush.Ani -- -i <input.ani> -o <output.ani>
dotnet run --project Crush.WebP -- -i <input.webp> -o <output.webp>
```

## CLI Reference

### Crush.Png

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

### Crush.Gif

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

### Crush.Tiff

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

### Crush.Bmp

| Option              | Short | Default            | Description                    |
| ------------------- | ----- | ------------------ | ------------------------------ |
| `--input`           | `-i`  | *(required)*       | Input BMP file path            |
| `--output`          | `-o`  | *(required)*       | Output BMP file path           |
| `--compression`     | `-c`  | `None,Rle8,Rle4`   | Compression methods to try     |
| `--auto-color-mode` | `-a`  | `true`             | Auto-select best color mode    |
| `--jobs`            | `-j`  | `0` (all cores)    | Max parallel tasks             |
| `--verbose`         | `-v`  | `false`            | Verbose output                 |

### Crush.Tga

| Option              | Short | Default         | Description                    |
| ------------------- | ----- | --------------- | ------------------------------ |
| `--input`           | `-i`  | *(required)*    | Input TGA file path            |
| `--output`          | `-o`  | *(required)*    | Output TGA file path           |
| `--compression`     | `-c`  | `None,Rle`      | Compression methods to try     |
| `--auto-color-mode` | `-a`  | `true`          | Auto-select best color mode    |
| `--jobs`            | `-j`  | `0` (all cores) | Max parallel tasks             |
| `--verbose`         | `-v`  | `false`         | Verbose output                 |

### Crush.Pcx

| Option              | Short | Default         | Description                    |
| ------------------- | ----- | --------------- | ------------------------------ |
| `--input`           | `-i`  | *(required)*    | Input PCX file path            |
| `--output`          | `-o`  | *(required)*    | Output PCX file path           |
| `--auto-color-mode` | `-a`  | `true`          | Auto-select best color mode    |
| `--jobs`            | `-j`  | `0` (all cores) | Max parallel tasks             |
| `--verbose`         | `-v`  | `false`         | Verbose output                 |

### Crush.Jpeg

| Option          | Short | Default              | Description                              |
| --------------- | ----- | -------------------- | ---------------------------------------- |
| `--input`       | `-i`  | *(required)*         | Input JPEG file path                     |
| `--output`      | `-o`  | *(required)*         | Output JPEG file path                    |
| `--lossy`       |       | `false`              | Enable lossy re-encoding mode            |
| `--min-quality` | `-q`  | `75`                 | Minimum quality for lossy mode (1-100)   |
| `--qualities`   |       | `75,80,85,90,95`     | Quality levels to try in lossy mode      |
| `--strip`       |       | `true`               | Try stripping metadata (EXIF, ICC, etc.) |
| `--jobs`        | `-j`  | `0` (all cores)      | Max parallel tasks                       |
| `--verbose`     | `-v`  | `false`              | Verbose output                           |

### Crush.Ico

| Option      | Short | Default         | Description                                       |
| ----------- | ----- | --------------- | ------------------------------------------------- |
| `--input`   | `-i`  | *(required)*    | Input ICO file path                               |
| `--output`  | `-o`  | *(required)*    | Output ICO file path                              |
| `--jobs`    | `-j`  | `0` (all cores) | Max parallel tasks                                |
| `--verbose` | `-v`  | `false`         | Verbose output                                    |

### Crush.Cur

| Option      | Short | Default         | Description                                       |
| ----------- | ----- | --------------- | ------------------------------------------------- |
| `--input`   | `-i`  | *(required)*    | Input CUR file path                               |
| `--output`  | `-o`  | *(required)*    | Output CUR file path                              |
| `--jobs`    | `-j`  | `0` (all cores) | Max parallel tasks                                |
| `--verbose` | `-v`  | `false`         | Verbose output                                    |

### Crush.Ani

| Option      | Short | Default         | Description                                       |
| ----------- | ----- | --------------- | ------------------------------------------------- |
| `--input`   | `-i`  | *(required)*    | Input ANI file path                               |
| `--output`  | `-o`  | *(required)*    | Output ANI file path                              |
| `--jobs`    | `-j`  | `0` (all cores) | Max parallel tasks                                |
| `--verbose` | `-v`  | `false`         | Verbose output                                    |

### Crush.WebP

| Option             | Short | Default         | Description                                       |
| ------------------ | ----- | --------------- | ------------------------------------------------- |
| `--input`          | `-i`  | *(required)*    | Input WebP file path                              |
| `--output`         | `-o`  | *(required)*    | Output WebP file path                             |
| `--strip-metadata` | `-s`  | `true`          | Strip metadata (EXIF, ICCP, XMP)                  |
| `--jobs`           | `-j`  | `0` (all cores) | Max parallel tasks                                |
| `--verbose`        | `-v`  | `false`         | Verbose output                                    |

## Inspiration & Format Coverage Reference

The format coverage of this project is inspired by the breadth of these tools:

- [Tom's Editor](https://tomseditor.com/convert/supported-formats) -- 600+ formats
- [ImageMagick](https://imagemagick.org/script/formats.php) -- 200+ formats
- [XnView](https://www.xnview.com/en/xnview/#formats) -- 500+ formats
- [IrfanView](https://www.irfanview.com/main_formats.htm) -- 100+ formats via plugins

## Planned Features

- ~~CancellationToken and progress reporting support~~ (done)
- ~~Unified GIF Frame type with palette-aware deduplication~~ (done)
- ~~BMP, TGA, PCX, JPEG format optimizers~~ (done)
- ~~FileFormat reader/writer pattern + shared CLI utilities (Phase 1 refactoring)~~ (done)
- ~~ICO format optimizer~~ (done)
- ~~CUR format optimizer~~ (done)
- ~~ANI format optimizer~~ (done)
- ~~WebP format optimizer (Phase 2: container-level optimization)~~ (done)
- ~~Struct-based binary headers with ReadFrom/WriteTo/GetFieldMap for hex editor field coloring~~ (done)
- ~~Project rename to consistent FileFormat.{Format}/Optimizer.{Format}/Crush.{Format} naming~~ (done)
- ~~33 new FileFormat libraries (QOI through PSD) for comprehensive image format coverage (Wave 1)~~ (done)
- ~~26 new FileFormat libraries (HRZ through DICOM) + ILBM HAM/EHB enhancement (Wave 2)~~ (done)
- Generic Optimizer/Crush for all FileFormat libraries
- Native C# TIFF/JPEG codec implementations (replace BitMiracle dependencies)
- NuGet packaging for all FileFormat libraries
- Cross-platform support (replace `System.Drawing.Common` with a portable decoder)

## Known Limitations

- **Windows only** - Optimizer.Png and Optimizer.Gif use `System.Drawing.Common` which requires Windows. Compression.Core, Optimizer.Tiff, Optimizer.Bmp, Optimizer.Tga, Optimizer.Pcx, and Optimizer.Jpeg have no platform restrictions beyond `EnableWindowsTargeting` for `System.Drawing.Common` pixel input.
- **Lossy sub-byte modes** - Sub-byte Grayscale (1/2/4-bit) quantizes values, which can lose precision for mid-range grays.
- **No 16-bit support** - Only 8-bit (and sub-byte 1/2/4-bit) depths are supported.
- **JPEG Chroma422** - BitMiracle.LibJpeg.NET does not implement 4:2:2 chroma subsampling; only 4:4:4 and 4:2:0 are supported.
