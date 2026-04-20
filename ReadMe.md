# PNGCrushCS

[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![Release](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/release.yml/badge.svg)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS?label=release&sort=semver)](https://github.com/Hawkynt/PNGCrushCS/releases/latest)
[![Latest nightly](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS?include_prereleases&label=nightly&sort=date)](https://github.com/Hawkynt/PNGCrushCS/releases?q=prerelease%3Atrue)
![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)
![Language](https://img.shields.io/github/languages/top/Hawkynt/PNGCrushCS?color=purple)
[![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/PNGCrushCS?branch=main)![Activity](https://img.shields.io/github/commit-activity/y/Hawkynt/PNGCrushCS?branch=main)](https://github.com/Hawkynt/PNGCrushCS/commits/main)
[![Downloads](https://img.shields.io/github/downloads/Hawkynt/PNGCrushCS/total)](https://github.com/Hawkynt/PNGCrushCS/releases)

> A C# image optimization suite that reduces file sizes by exhaustively testing combinations of compression parameters and selecting the smallest valid result. Supports **PNG**, **GIF**, **TIFF**, **BMP**, **TGA**, **PCX**, **JPEG**, **ICO**, **CUR**, **ANI**, and **WebP** optimization through a shared architecture with a custom Zopfli-class DEFLATE encoder. Includes **542 file format libraries** covering modern, professional, retro, and exotic image formats with full pixel codec implementations for complex formats (WebP VP8/VP8L, JPEG 2000, JPEG XL, JPEG-LS, JPEG XR, HEIF/HEVC, AVIF/AV1, BPG, FLIF, JBIG2, DjVu, ECW).

## How It Works

Each optimizer follows the same strategy: generate every valid combination of encoding parameters, compress each in parallel, and keep the smallest result.

### Unified Image Optimizer (Crush.Image)

The unified CLI auto-detects input format from magic bytes (with extension fallback), optimizes in the original format, and optionally tries cross-format conversion to find the smallest lossless output. Supports 86+ input formats via universal `RawImage`-based loading with `PixelConverter` (SIMD-accelerated), and 62+ writable conversion targets including lossless raster, professional, indexed, retro, and GPU texture formats. Formats with dedicated optimizers (PNG, GIF, TIFF, BMP, TGA, PCX, JPEG, ICO, CUR, ANI, WebP) use full optimization pipelines; other formats use `FromRawImage`/`ToBytes` conversion with optional quantization via FrameworkExtensions (Wu/Octree/MedianCut/NeuQuant/PngQuant quantizers + FloydSteinberg/Atkinson/Sierra/Bayer4x4 ditherers).

### PNG

1. Loads a PNG and extracts ARGB pixel data.
2. Analyzes image statistics (unique colors, alpha, grayscale detection, transparent key color).
3. Generates all valid optimization combos (color mode x bit depth x filter strategy x deflate method x interlace x optional quantizer/ditherer).
4. Tests each combo in parallel: convert pixels, apply filters, compress, assemble PNG byte stream.
5. Optional two-phase optimization: screen with fast compression first, then re-test top N candidates with expensive Ultra/Hyper methods.
6. Returns the smallest valid result.

### GIF

1. Parses input GIF via the `GifFileFormat` reader (header, LSD, GCT, frames with LCT, LZW decode, interlace deinterleaving).
2. Generates combos: palette reorder strategy x color table mode x disposal optimization x margin trimming x frame differencing x deferred clear codes.
3. Tests each combo in parallel: reorder palette, remap pixels, optimize frames, LZW-encode, assemble GIF.
4. Returns the smallest result.

### TIFF

1. Opens source TIFF via LibTiff.NET, extracts pixels, analyzes stats.
2. Generates combos: color mode x compression x predictor x strip/tile size (with invalid combo pruning).
3. Tests each combo in parallel: convert pixels, compress strips/tiles (PackBits/LZW/DEFLATE/Zopfli), assemble TIFF.
4. Returns the smallest result.

### BMP

1. Loads a BMP and extracts ARGB pixel data.
2. Analyzes image statistics (unique colors, grayscale detection).
3. Generates combos: color mode x compression x row order (with pruning: RLE8 only for Palette8, RLE4 only for Palette4).
4. Tests each combo in parallel: convert pixels, build palette, apply RLE if applicable, assemble BMP byte stream.
5. Returns the smallest valid result.

### TGA

1. Loads a TGA and extracts ARGB pixel data.
2. Detects alpha channel presence and grayscale content.
3. Generates combos: color mode x compression x origin (with pruning: Indexed8 only when <= 256 colors, Grayscale8 only when grayscale).
4. Tests each combo in parallel: convert pixels (BGRA/BGR/Grayscale/Indexed), apply pixel-width-aware RLE, assemble TGA with optional TGA 2.0 footer.
5. Returns the smallest valid result.

### PCX

1. Loads a PCX and extracts ARGB pixel data.
2. Analyzes unique color count for palette mode eligibility.
3. Generates combos: color mode x plane config x palette order (with pruning: SeparatePlanes only for RGB24, palette ordering only for indexed modes).
4. Tests each combo in parallel: convert pixels, RLE-encode scanlines (per-plane for RGB), assemble PCX with VGA palette.
5. Returns the smallest valid result.

### JPEG

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

### ICO

1. Reads an ICO file and parses all image entries (BMP DIB or PNG embedded).
2. Generates 2^n combinations (capped at 256) of BMP vs PNG format per entry.
3. Tests each combination in parallel: reassembles the ICO with the specified formats via IcoWriter.
4. Returns the smallest total file size.

### CUR

1. Reads a CUR file and parses all cursor image entries with hotspot coordinates.
2. CUR format is identical to ICO except type=2 in the header, and directory entry bytes 4-5/6-7 store HotspotX/HotspotY instead of planes/bitCount.
3. Generates 2^n combinations (capped at 256) of BMP vs PNG format per entry, preserving hotspot data.
4. Tests each combination in parallel: reassembles the CUR with the specified formats via CurWriter.
5. Returns the smallest total file size with all hotspot coordinates preserved.

### ANI

1. Reads an ANI animated cursor file (RIFF ACON container) and parses the anih header, optional rate/sequence chunks, and all embedded ICO frames.
2. Generates 2^n combinations (capped at 256) of BMP vs PNG format per image entry across all frames.
3. Tests each combination in parallel: reassembles the ANI with the specified entry formats, preserving rates and sequence data.
4. Returns the smallest total file size.

### WebP

Full pixel codec support with VP8L (lossless) and VP8 (lossy) decoders/encoders:

1. Reads a WebP file via the RIFF container parser, extracting VP8/VP8L image data and metadata chunks (ICCP, EXIF, XMP).
2. Decodes pixels: VP8L via LZ77 + Huffman + inverse transforms (SubtractGreen, Predictor, Color, ColorIndexing), VP8 via boolean arithmetic decoder + 4x4 IDCT + intra prediction + loop filter.
3. Generates combos: with metadata vs without metadata (strip EXIF/ICCP/XMP).
4. Tests each combo in parallel: rewrites the RIFF WEBP container with or without metadata chunks.
5. Returns the smallest valid result.

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
- **Multi-page support** - Read/write multi-IFD TIFF files, indexed page access via `IMultiImageFileFormat<TiffFile>`
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

- **Full VP8L codec** - Lossless decode/encode: LZ77 + canonical Huffman (5 groups), 14 predictor modes, Color/SubtractGreen/ColorIndexing transforms, 2D distance map with 120-entry cache
- **Full VP8 codec** - Lossy decode/encode: boolean arithmetic coder, 4x4 integer IDCT + Walsh-Hadamard DC, intra prediction (DC/V/H/TrueMotion for 4x4 and 16x16), normal/simple loop filter
- **Container-level optimization** - RIFF WEBP container rewriting with metadata stripping
- **Metadata stripping** - Removes EXIF, ICCP, XMP metadata chunks
- **VP8X extended format** - Reads and writes extended WebP with feature flags (alpha, animation, metadata)

### Shared

- **CrushRunner** - Common CLI runner (`Crush.Core`) handling input/output validation, cancellation, progress, timing, savings display
- **CancellationToken support** - All optimizers; CLIs wire Ctrl+C
- **IProgress reporting** - Real-time combo count, best size, phase updates
- **Parallel processing** - Concurrent combo testing with configurable parallelism
- **Memory-efficient** - `ArrayPool` throughout, concurrent best-result pattern, `stackalloc` for scoring
- **542 FileFormat libraries** - Standalone reader/writer for each format with full pixel codecs for complex formats (see Architecture for full list)
- **FileFormat.Riff** - Shared RIFF container reader/writer used by FileFormat.Ani and FileFormat.WebP
- **FileFormat.Iff** - IFF container reader/writer for Amiga ILBM and related formats
- **FileFormat.Core** - Shared header field descriptor types for hex editor field coloring, with `[HeaderField]`/`[HeaderFiller]` attributes and `HeaderFieldMapper` for reflection-based field map generation. Also provides `IImageFileFormat<TSelf>` with `static virtual` members for `Capabilities` and `MatchesSignature`, `IMultiImageFileFormat<TSelf>` for multi-image/multi-page formats (`ImageCount`, indexed `ToRawImage`), `[FormatMagicBytes]`/`[FormatDetectionPriority]` attributes, `FormatCapability` flags (including `MultiImage`), GPU block decoders (BC1-7, ETC1/2, ASTC, PVRTC), and SIMD-accelerated `PixelConverter` — enabling fully self-describing format libraries discovered automatically by the registry
- **BitmapConverter** - Cross-format bitmap loading for TGA, PCX, QOI, Farbfeld, and BMP via native FileFormat readers (no GDI+ dependency for these formats)

## Architecture

~1100-project solution across two repositories:

```
All 11 Optimizers + FileFormat.Qoi + FileFormat.Farbfeld <-- Optimizer.Image <-- Crush.Image

Compression.Core  <-- FileFormat.Png  <-- Optimizer.Png
                  <-- FileFormat.Tiff <-- Optimizer.Tiff

GifFileFormat (AnythingToGif repo) <-- Optimizer.Gif

FileFormat.Bmp  <-- Optimizer.Bmp
FileFormat.Tga  <-- Optimizer.Tga
FileFormat.Pcx  <-- Optimizer.Pcx
FileFormat.Jpeg <-- Optimizer.Jpeg
FileFormat.Jpeg <-- FileFormat.Mpo (MPO container wraps JPEG images)

FileFormat.Ico (uses FileFormat.Bmp + FileFormat.Png) <-- Optimizer.Ico
FileFormat.Cur (uses FileFormat.Ico)                  <-- Optimizer.Cur

FileFormat.Riff + FileFormat.Ico <-- FileFormat.Ani <-- Optimizer.Ani
FileFormat.Riff <-- FileFormat.WebP <-- Optimizer.WebP
FileFormat.Iff <-- FileFormat.Ilbm, FileFormat.IffPbm, FileFormat.IffAcbm, FileFormat.IffDeep, FileFormat.IffRgb8

FileFormat.Bmp + FileFormat.Ico + FileFormat.Core <-- FileFormat.WindowsPe (PE resource extraction)
FileFormat.Jpeg + FileFormat.Ccitt + Compression.Core <-- FileFormat.Pdf (embedded image extraction)

FileFormat.Core <-- All FileFormat libraries (header field mapping)

FileFormat.Pcx <-- FileFormat.Dcx (multi-page PCX container)
FileFormat.Png + Compression.Core <-- FileFormat.Apng (animated PNG)
FileFormat.Png <-- FileFormat.Mng (MNG VLC subset with embedded PNG frames)
FileFormat.Png <-- FileFormat.Icns (Apple icon container with embedded PNG entries)

Standalone FileFormat libraries (reader/writer only, no optimizer):
  Qoi, Farbfeld, Wbmp, Netpbm, Xbm, Xpm, MacPaint,
  ZxSpectrum, Koala, Degas, Neochrome, GemImg, AmstradCpc,
  Pfm, Sgi, SunRaster, Hdr, UtahRle, DrHalo, Iff, Ilbm,
  Fli, Flif, Cineon, Dds, Vtf, Ktx, Exr, Dpx, Fits, Ccitt,
  BbcMicro, C64Multi, Psd, Hrz, Cmu, Mtv, Qrt, Msp,
  Dcx, Astc, Pkm, Tim, Tim2, Wal, Pvr, Wpg, Bsave,
  Clp, Spectrum512, Tiny, Uhdr, Sixel, Wad, Wad3, Apng, Mng,
  Xcf, Pict, Dicom, DjVu, Avs, Otb, AliasPix, Xwd, ScitexCt,
  Jng, Viff, Rla, Nifti, CrackArt, Art, Sff, Acorn, Cals, Oric,
  Miff, Vicar, Nrrd, AppleII, AppleIIgs, Msx, OpenRaster, Palm,
  SamCoupe, Aai, Rgf, Fbm, Gbr, Pat, Xyz, Lss16, ColoRix,
  SunIcon, Cel, AmigaIcon, Gaf, GunPaint, GeoPaint,
  Psb, Icns, Blp, Fsh, Mpo, Pds, Ics, BioRadPic, Ptif,
  JpegLs, JpegXl, Jbig, Wsq, DjVu, Jbig2, Flif, Jpeg2000, JpegXr,
  Heif, Avif, Bpg, Dng, CameraRaw,
  Krita, Analyze, MetaImage, Envi, Eps, Wmf, Emf, Vips, QuakeSpr,
  NesChr, GameBoyTile, Atari8Bit, AtariFalcon, AtariFalconXga, CokeAtari, SpookySpritesFalcon, IffAnim,
  SoftImage, MayaIff, Xcursor, IffPbm, IffDeep, IffRgb8,
  IffRgbn, Interfile, Trs80, SnesTile, SegaGenTile,
  PcEngineTile, MasterSystemTile, SymbianMbm, XvThumbnail,
  Mrc, Gd2, BigTiff, AutodeskCel, Wad2,
  GbaTile, WonderSwanTile, NeoGeoSprite, NdsTexture, VirtualBoyTile,
  Vic20, Dragon, JupiterAce, Zx81, C128, C16Plus4, Electronika, Vector06c,
  TiBitmap, HpGrob, EpaBios, CiscoIp, PocketPc2bp,
  Mag, Pi, Q0,
  NokiaLogo, NokiaNlm, SiemensBmx, PsionPic,
  KofaxKfx, BrooktroutFax, WinFax, EdmicsC4,
  PixarRib, Sdt, MatLab, Ipl,
  Vivid, Bob, GfaRaytrace, Cloe,
  QuakeLmp, HalfLifeMdl, HereticM8, DoomFlat,
  Pdf, WindowsPe,
  FaxG3, StelaRaw, MonoMagic, Ecw,
  Thomson, CommodorePet, FmTowns, Pc88, Enterprise128, Atari7800,
  SharpX68k, RiscOsSprite, NeoGeoPocket, Atari2600,
  Doodle, DoodleComp, MicroIllustrator, Vidcom64, Picasso64,
  InterPaintHi, InterPaintMc, AdvancedArtStudio, RunPaint, Bfli,
  FunPainter, DrazPaint, GigaPaint, PrintfoxPagefox,
  Stad, PortfolioGraphics, PrintShop, IconLibrary, YuvRaw, ZeissLsm,
  Ioca, SpotImage, ImageSystem, ZeissBivas, PrintMaster,
  Artist64, FacePainter, FunGraphicsMachine, GoDot4Bit, HiresC64,
  EggPaint, CDUPaint, RainbowPainter, KoalaCompressed,
  DoodleAtari, Spectrum512Comp, Spectrum512Smoosh, DaliRaw, Gigacad,
  MegaPaint, FontasyGrafik, ArtDirector,
  AndrewToolkit, MgrBitmap, FaceServer, DbwRender, SbigCcd,
  Cp8Gray, ComputerEyes, PmBitmap,
  DivGameMap, HomeworldLif, Ps2Txc, HayesJtfax, GammaFax,
  CompW, DolphinEd, NokiaGroupGraphics,
  Im5Visilog, Pco16Bit, HiEddi, PaintMagic, SaracenPaint,
  AttGroup4, AccessFax, AdTechFax, BfxBitware, BrotherFax,
  CanonNavFax, EverexFax, FaxMan, FremontFax, ImagingFax,
  MobileFax, OazFax, OlicomFax, RicohFax, SciFax,
  SmartFax, Tg4, TeliFax, VentaFax, WorldportFax,
  NistIHead, AvhrrImage, ByuSir, Grs16, CsvImage,
  HfImage, LucasFilm, QuantelVpb,
  RedStormRsb, SegaSj1, SonyMavica, Pic2, FunPhotor,
  AtariGrafik, Calamus, NewsRoom,
  AdexImage, AimGrayScale, QdvImage, SifImage, WebShots, Rlc2, SeqImage

Crush.Core <-- Crush.Image + All 11 Optimizers
Crush.TestUtilities <-- Optimizer test projects

Tests/ directory contains all ~467 test projects

FrameworkExtensions.System.Drawing (NuGet) --> Optimizer.Png
BitMiracle.LibTiff.NET (NuGet) --> FileFormat.Tiff
BitMiracle.LibJpeg.NET (NuGet) --> FileFormat.Jpeg
```

| Project                  | TFM             | Type    | Description                                                                                           |
| ------------------------ | --------------- | ------- | ----------------------------------------------------------------------------------------------------- |
| **Compression.Core**     | net8.0          | Library | Pure RFC 1951 DEFLATE with Zopfli-class optimal parsing. No platform dependencies.                    |
| **Crush.Core**           | net8.0          | Library | Shared CLI utilities: `CrushRunner`, `ICrushOptions`, `OptimizationProgress`, `FileFormatting`.       |
| **Crush.TestUtilities**  | net8.0          | Library | Shared test helpers: `TestBitmapFactory`, `TempFileScope`.                                            |
| **FileFormat.Core**      | net8.0          | Library | Shared data types and attribute-based header field mapping (`HeaderFieldDescriptor`, `HeaderFieldAttribute`, `HeaderFillerAttribute`, `HeaderFieldMapper`). |
| **FileFormat.Png**       | net8.0          | Library | PNG file format reader/writer with CRC32, chunk parsing, Adam7 support.                               |
| **Optimizer.Png**        | net10.0-windows | Library | PNG optimization engine with unsafe pixel access, FrameworkExtensions dithering integration.          |
| **Optimizer.Gif**        | net8.0-windows  | Library | GIF optimization engine with palette reordering, frame optimization, LZW re-encoding.                 |
| **Optimizer.Tiff**       | net8.0          | Library | TIFF optimization with PackBits, LZW, DEFLATE/Zopfli, tiled encoding.                                 |
| **Optimizer.Bmp**        | net8.0          | Library | BMP optimization with RLE4/RLE8 compression, 7 color modes, row order variants.                       |
| **Optimizer.Tga**        | net8.0          | Library | TGA optimization with pixel-width-aware RLE, 5 color modes, origin variants.                          |
| **Optimizer.Pcx**        | net8.0          | Library | PCX optimization with RLE encoding, plane configurations, palette ordering.                            |
| **Optimizer.Jpeg**       | net8.0          | Library | JPEG optimization with lossless DCT transfer and lossy re-encoding via LibJpeg.NET.                   |
| **FileFormat.Ico**       | net8.0          | Library | ICO file format reader/writer with BMP DIB and PNG embedding support.                                  |
| **Optimizer.Ico**        | net8.0          | Library | ICO optimization engine with per-entry BMP/PNG format selection.                                       |
| **FileFormat.Cur**       | net8.0          | Library | CUR file format reader/writer with hotspot support. Reuses FileFormat.Ico internals.                   |
| **Optimizer.Cur**        | net8.0          | Library | CUR optimization engine with per-entry BMP/PNG format selection and hotspot preservation.               |
| **FileFormat.Ani**       | net8.0          | Library | ANI animated cursor file format reader/writer using RIFF container and FileFormat.Ico.                  |
| **Optimizer.Ani**        | net8.0          | Library | ANI optimization engine with per-entry BMP/PNG format selection across all animation frames.             |
| **FileFormat.WebP**      | net8.0          | Library | WebP file format reader/writer with full VP8L (lossless) and VP8 (lossy) pixel codecs.                  |
| **Optimizer.WebP**       | net8.0          | Library | WebP optimization engine with metadata stripping and RIFF container rewriting.                           |
| **FileFormat.Pdf**       | net8.0          | Library | PDF embedded image extractor: xref/stream parsing, FlateDecode/DCTDecode/CCITTFaxDecode/ASCII85/Hex.    |
| **FileFormat.WindowsPe** | net8.0          | Library | PE (EXE/DLL) resource image extractor: icons, cursors, bitmaps, embedded images from .rsrc section.     |
| **Optimizer.Image**      | net10.0-windows | Library | Universal image optimization engine: auto-detect format, same-format + cross-format conversion. Uses `BitmapConverter` for native TGA/PCX/QOI/Farbfeld loading. |
| **Crush.Image**          | net10.0-windows | Console | Unified CLI replacing all 11 format-specific tools. Verb-based dispatch with format-specific options.    |
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
| **FileFormat.CrackArt**  | net8.0          | Library | Atari ST CrackArt packed image reader/writer: 33-byte header, PackBits RLE, .ca1/.ca2/.ca3.             |
| **FileFormat.Neochrome** | net8.0          | Library | Atari ST NEOchrome reader/writer: 128-byte header + 32000 bytes planar.                                |
| **FileFormat.SyntheticArts** | net8.0      | Library | Atari ST Synthetic Arts reader/writer: 640x200, 4 colors, 2-plane medium resolution, 32032 bytes.      |
| **FileFormat.HighresMedium** | net8.0      | Library | Atari ST Highres Medium interlaced reader/writer: 640x200, 2 frames blended, 64064 bytes.              |
| **FileFormat.FullscreenKit** | net8.0      | Library | Atari ST Fullscreen Construction Kit overscan reader/writer: 416x274 or 448x272, 16 colors, 4 planes.  |
| **FileFormat.PabloPaint** | net8.0         | Library | Atari ST Pablo Paint reader/writer: 640x400 monochrome, 32000 bytes raw bitmap, .pa3.                  |
| **FileFormat.QuantumPaint** | net8.0       | Library | Atari ST QuantumPaint reader/writer: 320x200, 16 colors, 32-byte palette + 32000 bytes planar, .pbx.   |
| **FileFormat.SinbadSlideshow** | net8.0    | Library | Atari ST Sinbad Slideshow reader/writer: 320x200, 16 colors, 32-byte palette + 32000 bytes planar, .ssb. |
| **FileFormat.GemImg**    | net8.0          | Library | GEM Raster Image reader/writer: scan-line encoding with vertical RLE and pattern replication.            |
| **FileFormat.GunPaint**  | net8.0          | Library | C64 GunPaint FLI multicolor bitmap reader/writer: fixed 33603-byte format, 160x200, raw container with simplified multicolor decode. |
| **FileFormat.AmstradCpc** | net8.0         | Library | Amstrad CPC screen memory dump reader/writer with CPC memory interleave and pixel packing for Mode 0/1/2. |
| **FileFormat.Pfm**       | net8.0          | Library | Portable Float Map reader/writer: text header, float32 pixels, endianness from scale sign.              |
| **FileFormat.Sgi**       | net8.0          | Library | Silicon Graphics Image reader/writer: 512-byte header, channel-plane RLE, big-endian.                   |
| **FileFormat.SunRaster** | net8.0          | Library | Sun Raster reader/writer: 32-byte header, escape-based RLE (0x80), big-endian.                         |
| **FileFormat.Hdr**       | net8.0          | Library | Radiance HDR/RGBE reader/writer: text header, RGBE encoding, scanline RLE.                              |
| **FileFormat.UtahRle**   | net8.0          | Library | Utah Raster Toolkit reader/writer: multi-channel scanline operations.                                   |
| **FileFormat.DrHalo**    | net8.0          | Library | Dr. Halo CUT reader/writer: 8-bit indexed, per-scanline RLE, separate .PAL palette.                    |
| **FileFormat.IffAcbm**   | net8.0          | Library | IFF ACBM (Amiga Contiguous Bitmap) reader/writer: FORM ACBM with BMHD, CMAP, ABIT contiguous bitplane data. |
| **FileFormat.IffPbm**    | net8.0          | Library | IFF PBM (Packed Bitmap) reader/writer: FORM PBM with BMHD, CMAP, chunky 8-bit BODY, ByteRun1 compression. References FileFormat.Iff. |
| **FileFormat.IffDeep**   | net8.0          | Library | IFF DEEP (TVPaint/NewTek) reader/writer: FORM DEEP with DGBL/DPEL/DBOD chunks, RGB24/RGBA32, ByteRun1 RLE. References FileFormat.Iff. |
| **FileFormat.IffRgb8**   | net8.0          | Library | IFF RGB8 (NewTek/Impulse) reader/writer: FORM RGB8 with BMHD, 4-byte pixel group ByteRun1, Rgb24. References FileFormat.Iff. |
| **FileFormat.Ilbm**      | net8.0          | Library | IFF ILBM reader/writer: planar bitmap, ByteRun1 (PackBits), CAMG chunk, HAM6/HAM8 and EHB modes.       |
| **FileFormat.Ingr**      | net8.0          | Library | Intergraph Raster (.cit/.itg) reader/writer: 512-byte header, uncompressed 8-bit grayscale and 24-bit RGB. |
| **FileFormat.Fli**       | net8.0          | Library | Autodesk FLI/FLC animation reader/writer: frame-differential encoding.                                  |
| **FileFormat.Flif**      | net8.0          | Library | FLIF (Free Lossless Image Format) reader/writer: "FLIF" magic, varint dimensions, Gray/RGB/RGBA, deflate-compressed pixel data. |
| **FileFormat.Fsh**       | net8.0          | Library | EA Sports FSH (Shape/Texture) archive reader/writer: SHPI magic, ARGB8888/RGB888/RGB565/Indexed8/DXT1/DXT3. |
| **FileFormat.Cineon**    | net8.0          | Library | Kodak Cineon reader/writer: 1024-byte header, 10-bit log film scanning.                                |
| **FileFormat.Dds**       | net8.0          | Library | DirectDraw Surface reader/writer: 128-byte header, optional DX10 header, GPU textures.                  |
| **FileFormat.Vtf**       | net8.0          | Library | Valve Texture Format reader/writer: VTF 7.x, mipmaps, BCn + custom formats.                            |
| **FileFormat.Ktx**       | net8.0          | Library | KTX1 + KTX2 GPU texture container reader/writer for OpenGL/Vulkan.                                     |
| **FileFormat.Exr**       | net8.0          | Library | OpenEXR reader/writer: single-part scanline, None compression, Half/Float/UInt.                         |
| **FileFormat.Dpx**       | net8.0          | Library | Digital Picture Exchange reader/writer: 2048-byte header, 10-bit packed pixels.                         |
| **FileFormat.Fits**      | net8.0          | Library | FITS (astronomy) reader/writer: 80-char card headers, multi-dimensional arrays.                         |
| **FileFormat.Ccitt**     | net8.0          | Library | CCITT Group 3/4 fax compression reader/writer: Huffman-coded 1bpp bi-level.                             |
| **FileFormat.BbcMicro**  | net8.0          | Library | BBC Micro screen dump reader/writer: character-block layout, Mode 0/1/2/4/5.                            |
| **FileFormat.BioRadPic** | net8.0          | Library | Bio-Rad PIC confocal microscopy reader/writer: 76-byte LE header, 8/16-bit grayscale.                   |
| **FileFormat.C64Multi**  | net8.0          | Library | C64 multiformat art reader/writer: Art Studio Hires/Multicolor.                                        |
| **FileFormat.FliEditor** | net8.0          | Library | C64 FLI Editor (.fed) reader/writer: 160x200 multicolor FLI, 8 screen banks of 1000 bytes.             |
| **FileFormat.FliDesigner** | net8.0        | Library | C64 FLI Designer (.fd2) reader/writer: 160x200 multicolor FLI, 8 screen banks of 1000 bytes.           |
| **FileFormat.MuifliEditor** | net8.0       | Library | C64 MUIFLI Editor (.muf/.mui/.mup) reader/writer: 160x200 interlace FLI, 2 frames, 1024-byte banks.   |
| **FileFormat.Psd**       | net8.0          | Library | Adobe Photoshop reader/writer: flat composite image, RLE/Raw, 8 color modes.                            |
| **FileFormat.Ptif**      | net8.0          | Library | PTIF (Pyramid TIFF) reader/writer: first-IFD uncompressed TIFF, Gray8/RGB24/RGBA32, LE+BE read, LE write. |
| **FileFormat.Hrz**       | net8.0          | Library | Slow-Scan Television reader/writer: 256x240 fixed, raw RGB, no header, 184,320 bytes exact.             |
| **FileFormat.Ics**       | net8.0          | Library | ICS (Image Cytometry Standard) reader/writer: tab-separated text header, 8-bit Gray/RGB, uncompressed/gzip. |
| **FileFormat.Cmu**       | net8.0          | Library | CMU Window Manager Bitmap reader/writer: 8-byte header, 1bpp packed MSB-first, big-endian.              |
| **FileFormat.Mtv**       | net8.0          | Library | MTV Ray Tracer reader/writer: ASCII "width height\n" header, raw RGB pixel data.                        |
| **FileFormat.Qrt**       | net8.0          | Library | QRT Ray Tracer reader/writer: 10-byte header, raw RGB pixel data.                                       |
| **FileFormat.Qtif**      | net8.0          | Library | QTIF (QuickTime Image) reader/writer: atom-based container, idsc/idat atoms, uncompressed RGB24.        |
| **FileFormat.Msp**       | net8.0          | Library | Microsoft Paint v1/v2 reader/writer: 32-byte header, 1bpp monochrome, V2 RLE compression.              |
| **FileFormat.Dcx**       | net8.0          | Library | Multi-page PCX container reader/writer: 0x3ADE68B1 magic, up to 1023 page offsets. References FileFormat.Pcx. |
| **FileFormat.Astc**      | net8.0          | Library | Adaptive Scalable Texture Compression reader/writer: 16-byte header, 0x5CA1AB13 magic, raw ASTC blocks. |
| **FileFormat.Pkm**       | net8.0          | Library | Ericsson Texture Container reader/writer: 16-byte header, "PKM " magic, ETC1/ETC2 blocks.              |
| **FileFormat.Tim**       | net8.0          | Library | PlayStation 1 Texture reader/writer: 8-byte header, 0x10 magic, 4/8/16/24-bit modes, optional CLUT.    |
| **FileFormat.Tim2**      | net8.0          | Library | PlayStation 2/PSP Texture reader/writer: "TIM2" magic, 16-byte file header, 48-byte picture headers.   |
| **FileFormat.Wal**       | net8.0          | Library | Quake 2 Texture reader/writer: 100-byte header, 8-bit indexed, 4 mipmap levels, no embedded palette.   |
| **FileFormat.Pvr**       | net8.0          | Library | PowerVR Texture v3 reader/writer: 52-byte header, GPU texture container.                                |
| **FileFormat.Wpg**       | net8.0          | Library | WordPerfect Graphics reader/writer: 16-byte header, record-based, RLE bitmap records.                  |
| **FileFormat.Wsq**       | net8.0          | Library | WSQ (Wavelet Scalar Quantization) fingerprint image reader/writer: CDF 9/7 DWT, scalar quantization, Huffman coding. |
| **FileFormat.Bsave**     | net8.0          | Library | IBM PC BSAVE Graphics reader/writer: 7-byte header, 0xFD magic, screen memory dump with mode detection. |
| **FileFormat.Clp**       | net8.0          | Library | Windows Clipboard reader/writer: 4-byte header, format directory, embedded DIB data.                   |
| **FileFormat.ColoRix**   | net8.0          | Library | ColoRIX VGA paint reader/writer: "RIX3" magic, 10-byte header, 256-color VGA palette, RLE compression. |
| **FileFormat.Spectrum512** | net8.0        | Library | Atari ST 512-color reader/writer: 51,104 bytes, 320x199, 48 palettes per scanline.                     |
| **FileFormat.Tiny**      | net8.0          | Library | Atari ST Compressed DEGAS reader/writer: resolution byte + palette + delta+word-level RLE.              |
| **FileFormat.Uhdr**      | net8.0          | Library | UHDR (Ultra HDR) reader/writer: 16-byte header ("UHDR" magic, version, width, height), raw RGB24 data. |
| **FileFormat.Sixel**     | net8.0          | Library | DEC Terminal Graphics reader/writer: text-based encoding, ESC P sixel-data ESC \, 6-pixel bands.        |
| **FileFormat.Wad**       | net8.0          | Library | Doom WAD container reader/writer: "IWAD"/"PWAD" magic, 12-byte header, named lumps.                    |
| **FileFormat.Wad3**      | net8.0          | Library | Half-Life WAD3 texture container reader/writer: "WAD3" magic, MipTex with embedded palette.            |
| **FileFormat.Apng**      | net8.0          | Library | Animated PNG reader/writer: extends PNG with acTL/fcTL/fdAT chunks. References FileFormat.Png + Compression.Core. |
| **FileFormat.Mng**       | net8.0          | Library | Multiple Network Graphics VLC subset reader/writer: MNG signature, MHDR chunk, embedded PNG frames.    |
| **FileFormat.Xcf**       | net8.0          | Library | GIMP Native (flat composite) reader/writer: "gimp xcf" magic, 64x64 tiles, per-channel RLE/zlib.      |
| **FileFormat.Pict**      | net8.0          | Library | Apple QuickDraw (raster subset) reader/writer: 512-byte preamble, PICT2 opcodes, PackBits.             |
| **FileFormat.Dicom**     | net8.0          | Library | Medical Imaging (basic subset) reader/writer: 128-byte preamble + "DICM", tag-length-value, Explicit VR LE. |
| **FileFormat.DjVu**      | net8.0          | Library | DjVu single-page image reader/writer: IFF85 container ("AT&T" magic), FORM:DJVU, INFO chunk, PM44 pixel data. |
| **FileFormat.Miff**      | net8.0          | Library | ImageMagick MIFF reader/writer: text header ("id=ImageMagick"), key=value lines, None/RLE/Zip compression.   |
| **FileFormat.OpenRaster** | net8.0         | Library | OpenRaster (.ora) reader/writer: ZIP container, PNG layers, XML manifest, layer position/opacity/visibility. |
| **FileFormat.Art**       | net8.0          | Library | Build Engine ART tile archive reader/writer: 16-byte header, column-major 8-bit indexed tiles, animation data. |
| **FileFormat.Avs**       | net8.0          | Library | AVS (Application Visualization System) reader/writer: 8-byte header, raw ARGB pixels, big-endian.           |
| **FileFormat.Otb**       | net8.0          | Library | OTB (Nokia Over-The-Air Bitmap) reader/writer: 4-byte header, 1bpp MSB-first, max 255x255.                  |
| **FileFormat.AliasPix**  | net8.0          | Library | Alias/Wavefront PIX reader/writer: 10-byte BE header, per-scanline RLE, 24/32bpp.                           |
| **FileFormat.Xwd**       | net8.0          | Library | XWD (X Window Dump) reader/writer: 100-byte variable header (v7), optional colormap, raw pixels.            |
| **FileFormat.ScitexCt**  | net8.0          | Library | Scitex CT (Continuous Tone) reader/writer: 80-byte ASCII header, CMYK/RGB/Grayscale, raw pixel data.        |
| **FileFormat.Viff**      | net8.0          | Library | VIFF (Khoros Visualization Image File Format) reader/writer: 1024-byte header, 0xAB magic, multi-band, endianness-aware.   |
| **FileFormat.Jbig2**     | net8.0          | Library | JBIG2 (ITU-T T.88) bi-level image reader/writer: 8-byte magic, segment-based structure, MMR (CCITT G4) coded generic regions. |
| **FileFormat.Jng**       | net8.0          | Library | JNG (JPEG Network Graphics) reader/writer: 8-byte magic, PNG-style chunks, JHDR header, JDAT JPEG data, optional JDAA/IDAT alpha. |
| **FileFormat.Rla**       | net8.0          | Library | Wavefront RLA reader/writer: 740-byte big-endian header, per-scanline offset table, per-channel RLE.       |
| **FileFormat.Vicar**     | net8.0          | Library | VICAR (NASA JPL) reader/writer: ASCII keyword=value header (LBLSIZE self-describing), BYTE/HALF/FULL/REAL/DOUB pixel types, BSQ/BIL/BIP band organization. |
| **FileFormat.Nrrd**      | net8.0          | Library | NRRD (Nearly Raw Raster Data) reader/writer: text header + raw/gzip binary data, multi-dimensional arrays, int8-double types. |
| **FileFormat.Msx**       | net8.0          | Library | MSX2 screen dump reader/writer: SC2/SC5/SC7/SC8 modes, optional 7-byte BLOAD header, palette support. |
| **FileFormat.Analyze**   | net8.0          | Library | Analyze 7.5 medical imaging reader/writer: paired .hdr (348-byte LE header) + .img (raw pixels), UInt8/Int16/Int32/Float32/Rgb24 data types. |
| **FileFormat.Nifti**     | net8.0          | Library | NIfTI v1 neuroimaging reader/writer: 348-byte LE header, 9 data types, 2D/3D volumes, scaling slope/intercept. |
| **FileFormat.Nitf**      | net8.0          | Library | NITF (National Imagery Transmission Format) MIL-STD-2500C reader/writer: "NITF02.10" ASCII magic, fixed-width text headers, 8-bit grayscale/RGB band-sequential, uncompressed (IC=NC). |
| **FileFormat.Sff**       | net8.0          | Library | SFF (Structured Fax File) reader/writer: German ISDN fax container, 12-byte header, linked page list, 1bpp raw pixel data. |
| **FileFormat.Acorn**     | net8.0          | Library | Acorn RISC OS Sprite reader/writer: 12-byte area header, 44-byte sprite headers, multi-sprite, old/new mode word BPP, optional palette/mask. |
| **FileFormat.Cals**      | net8.0          | Library | CALS (MIL-STD-1840) raster reader/writer: 768-byte text header (6x128-byte records), 1bpp MSB-first pixel data. |
| **FileFormat.Oric**       | net8.0          | Library | Oric hi-res graphics screen dump reader/writer: 240x200, 40 bytes/row, 8000 bytes, no header.           |
| **FileFormat.Miff**      | net8.0          | Library | MIFF (ImageMagick Native) reader/writer: text header (id=ImageMagick), None/RLE/Zip compression, DirectClass/PseudoClass. |
| **FileFormat.AppleII**   | net8.0          | Library | Apple II Hi-Res Graphics reader/writer: HGR (8192 bytes, 280x192) and DHGR (16384 bytes, 560x192), memory interleave layout. |
| **FileFormat.AppleIIgs** | net8.0          | Library | Apple IIGS Super Hi-Res reader/writer: 32768 bytes, 320/640 mode, per-scanline palette selection, 16 palettes of 16 colors. |
| **FileFormat.Palm**      | net8.0          | Library | Palm OS Bitmap reader/writer: 16-byte BE header, 1/2/4/8/16 bpp, None/Scanline/RLE/PackBits compression, optional palette. |
| **FileFormat.Pcd**       | net8.0          | Library | PCD (Kodak Photo CD) reader/writer: 2048-byte preamble, "PCD_IPI" magic at offset 2048, raw RGB24 pixel data.       |
| **FileFormat.Bsb**       | net8.0          | Library | BSB/KAP nautical chart reader/writer: text header with key/value lines, indexed 8-bit palette, RLE pixel data.       |
| **FileFormat.Awd**       | net8.0          | Library | AWD (Microsoft Fax) reader/writer: "AWD\0" magic, 1bpp monochrome, packed MSB-first pixel data.                      |
| **FileFormat.Psp**       | net8.0          | Library | PSP (Paint Shop Pro) reader/writer: 32-byte magic, block-based structure, RGB24 composite image.                     |
| **FileFormat.PcPaint**   | net8.0          | Library | PC Paint/Pictor Page Format reader/writer: 0x1234 magic, 18-byte header, 256-color palette, RLE pixel data.         |
| **FileFormat.PalmPdb**   | net8.0          | Library | Palm PDB image database reader/writer: 78-byte BE header, "Img " type at offset 60, RGB24 pixel records.            |
| **FileFormat.PhotoPaint** | net8.0         | Library | Corel Photo-Paint CPT reader/writer: "CPT\0" magic, 24-byte header, uncompressed RGB24 pixel data.                  |
| **FileFormat.Pdn**       | net8.0          | Library | Paint.NET PDN reader/writer: "PDN3" magic, 16-byte header, gzip-compressed BGRA32 pixel data.                       |
| **FileFormat.Fpx**       | net8.0          | Library | FlashPix reader/writer: "FPX\0" magic, 16-byte header, uncompressed RGB24 pixel data.                               |
| **FileFormat.SamCoupe**  | net8.0          | Library | SAM Coupe screen dump reader/writer: Mode 3 (512x192, 2bpp) and Mode 4 (256x192, 4bpp), page-interleaved layout.    |
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
| **Optimizer.Image.Tests** | net10.0-windows | NUnit 4 | 167 tests: format detection (magic bytes + extensions for 178 formats), BitmapConverter (TGA/PCX/QOI/Farbfeld/BMP loading), optimizer E2E, data types, cancellation, progress. |
| **FileFormat.Core.Tests** | net8.0         | NUnit 4 | 13 tests: HeaderFieldMapper (simple/name override/filler/sorting/caching), HeaderFieldAttribute, HeaderFillerAttribute, HeaderFieldDescriptor equality. |
| **FileFormat.Bmp.Tests** | net8.0          | NUnit 4 | 28 tests: reader validation, writer structure, round-trip (7 color modes), RLE compressor, data types.  |
| **FileFormat.Tga.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer structure, round-trip (5 modes + RLE), RLE compressor, data types.  |
| **FileFormat.Pcx.Tests** | net8.0          | NUnit 4 | 22 tests: reader validation, writer structure, round-trip (4 modes), RLE compressor, data types.        |
| **FileFormat.Pds.Tests** | net8.0          | NUnit 4 | 48 tests: reader validation, writer structure, header parser, round-trip (grayscale/RGB BSQ/BIP/BIL/16-bit/file), data types. |
| **FileFormat.Jpeg.Tests** | net8.0         | NUnit 4 | 17 tests: reader validation, writer lossy/lossless, round-trip, data types.                             |
| **FileFormat.JpegLs.Tests** | net8.0       | NUnit 4 | 85 tests: reader validation, writer output, LOCO-I codec (MED predictor, gradient quantization, Golomb coding, context management, bias correction), round-trip (grayscale/RGB/gradient/file/stream/RawImage), data types. |
| **FileFormat.Jbig.Tests** | net8.0         | NUnit 4 | 68 tests: arithmetic coder (Qe table, encode/decode, state transitions), reader validation, writer output, context model, header tests, round-trip (white/black/checkerboard/diagonal/file/RawImage/TPBON), data types. |
| **FileFormat.JpegXl.Tests** | net8.0       | NUnit 4 | 58 tests: reader validation, writer output, size header encode/decode, round-trip (Gray8/Rgb24/gradient/file/RawImage), data types. |
| **FileFormat.JpegXr.Tests** | net8.0       | NUnit 4 | 61 tests: reader validation, writer output, IFD tests, round-trip (grayscale/RGB/gradient/file/stream/RawImage), data types. |
| **FileFormat.Heif.Tests** | net8.0         | NUnit 4 | 54 tests: reader validation, writer output, ISOBMFF box parser, round-trip, data types.                                     |
| **FileFormat.Avif.Tests** | net8.0         | NUnit 4 | 53 tests: reader validation, writer output, ISOBMFF box parser, round-trip, data types.                                     |
| **FileFormat.Bpg.Tests**  | net8.0         | NUnit 4 | 76 tests: reader validation, writer output, ue7 encoder/decoder, round-trip, data types.                                    |
| **FileFormat.Dng.Tests**  | net8.0         | NUnit 4 | 59 tests: reader validation, writer output, IFD entry parser, round-trip, data types.                                       |
| **FileFormat.CameraRaw.Tests** | net8.0    | NUnit 4 | 69 tests: reader validation, writer output, TIFF parser, round-trip, data types.                                            |
| **FileFormat.Krita.Tests** | net8.0        | NUnit 4 | 33 tests: reader validation, writer output, round-trip (ZIP container, PNG mergedimage), data types.                        |
| **FileFormat.Analyze.Tests** | net8.0      | NUnit 4 | 51 tests: reader validation, writer output, header tests, round-trip (grayscale/RGB/companion .img), data types.            |
| **FileFormat.MetaImage.Tests** | net8.0    | NUnit 4 | 52 tests: reader validation, writer output, header parser, round-trip (embedded/external/gzip), data types.                 |
| **FileFormat.Envi.Tests** | net8.0         | NUnit 4 | 74 tests: reader validation, writer output, header parser (multiline braces, interleave, data offset), round-trip (grayscale/RGB BSQ/BIP/BIL/file/RawImage), data types. |
| **FileFormat.Eps.Tests** | net8.0          | NUnit 4 | 31 tests: reader validation, writer output, round-trip (TIFF preview extraction), data types.                               |
| **FileFormat.Wmf.Tests** | net8.0          | NUnit 4 | 29 tests: reader validation, writer output, round-trip (DIB extraction), data types.                                        |
| **FileFormat.Emf.Tests** | net8.0          | NUnit 4 | 31 tests: reader validation, writer output, round-trip (DIB extraction), data types.                                        |
| **FileFormat.Vips.Tests** | net8.0         | NUnit 4 | 43 tests: reader validation, writer output, round-trip (Gray8/Rgb24/Rgba32), header tests, data types.                     |
| **FileFormat.QuakeSpr.Tests** | net8.0     | NUnit 4 | 41 tests: reader validation, writer output, round-trip (indexed sprites), data types.                                       |
| **FileFormat.NesChr.Tests** | net8.0       | NUnit 4 | 44 tests: reader validation, writer output, round-trip (2bpp planar tiles), data types.                                     |
| **FileFormat.GameBoyTile.Tests** | net8.0  | NUnit 4 | 43 tests: reader validation, writer output, round-trip (2bpp interleaved tiles), data types.                                |
| **FileFormat.Atari8Bit.Tests** | net8.0    | NUnit 4 | 43 tests: reader validation, writer output, round-trip (GR.7/GR.8/GR.9/GR.15 modes), data types.                          |
| **FileFormat.AtariFalcon.Tests** | net8.0  | NUnit 4 | 37 tests: reader validation, writer output, round-trip (RGB565 conversion, via file, via RawImage), data types.             |
| **FileFormat.CokeAtari.Tests** | net8.0    | NUnit 4 | 41 tests: reader validation, writer output, header tests, round-trip (RGB565, via file, via RawImage), data types.          |
| **FileFormat.AtariFalconXga.Tests** | net8.0 | NUnit 4 | 40 tests: reader validation, writer output, header tests, round-trip (RGB565, via file, via RawImage), data types.       |
| **FileFormat.SpookySpritesFalcon.Tests** | net8.0 | NUnit 4 | 50 tests: reader validation, writer output, header tests, RLE compressor, round-trip (compressed RGB565, via file, via RawImage), data types. |
| **FileFormat.IffAnim.Tests** | net8.0      | NUnit 4 | 34 tests: reader validation, writer output, round-trip (FORM ANIM with ILBM frames), data types.                           |
| **FileFormat.SoftImage.Tests** | net8.0    | NUnit 4 | 53 tests: reader validation, writer output, round-trip (mixed RLE, RGB/RGBA, via file, via RawImage), data types.          |
| **FileFormat.MayaIff.Tests** | net8.0      | NUnit 4 | 49 tests: reader validation, writer output, round-trip (RGBA/RGB, via file, via RawImage), data types.                     |
| **FileFormat.Xcursor.Tests** | net8.0      | NUnit 4 | 52 tests: reader validation, writer output, round-trip (premultiplied ARGB, via file, via RawImage), data types.           |
| **FileFormat.IffPbm.Tests** | net8.0       | NUnit 4 | 54 tests: reader validation, writer output, BMHD/ByteRun1, round-trip (chunky indexed, via file, via RawImage), data types. |
| **FileFormat.IffAcbm.Tests** | net8.0      | NUnit 4 | 57 tests: reader validation, writer output, planar converter, round-trip (contiguous bitplanes, via file, via RawImage), data types. |
| **FileFormat.IffDeep.Tests** | net8.0      | NUnit 4 | 71 tests: reader validation, writer output, ByteRun1 compressor, round-trip (RGB/RGBA, via file, via RawImage), data types. |
| **FileFormat.IffRgb8.Tests** | net8.0      | NUnit 4 | 49 tests: reader validation, writer output, Rgb8ByteRun1, round-trip (24-bit RGB, via file, via RawImage), data types.     |
| **FileFormat.Interfile.Tests** | net8.0    | NUnit 4 | 54 tests: reader validation, writer output, header parser, round-trip (Gray8/Rgb24, via file, via RawImage), data types.   |
| **FileFormat.Mpo.Tests**  | net8.0         | NUnit 4 | 33 tests: reader validation, writer output, round-trip (single/multi-image, via file, via RawImage, grayscale), data types. |
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
| **FileFormat.CrackArt.Tests** | net8.0     | NUnit 4 | 31 tests: reader validation, writer output, compressor round-trip, round-trip, header tests, data types. |
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
| **FileFormat.Ingr.Tests** | net8.0         | NUnit 4 | 38 tests: reader validation (null, missing, too small, unsupported data type, valid RGB24/Gray8, stream, extent fallback), writer output (null, header size, header type, data type field, offsets, pixel data, total size), round-trip (RGB24, Gray8, via file, via RawImage, large image), data type tests (enum values, defaults, extensions, null validation). |
| **FileFormat.Fli.Tests** | net8.0          | NUnit 4 | 37 tests: reader validation, writer output, delta decoder, round-trip, header tests.                   |
| **FileFormat.Flif.Tests** | net8.0         | NUnit 4 | 67 tests: reader validation, writer output, varint encode/decode, round-trip (Gray/RGB/RGBA/file/RawImage), data types. |
| **FileFormat.Fsh.Tests** | net8.0          | NUnit 4 | 60 tests: reader validation, writer output, pixel data size calculations, round-trip, data type tests. |
| **FileFormat.Cineon.Tests** | net8.0       | NUnit 4 | 23 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Dds.Tests** | net8.0          | NUnit 4 | 50 tests: reader validation, writer output, block info, round-trip, header/pixel format/DX10 tests.    |
| **FileFormat.Vtf.Tests** | net8.0          | NUnit 4 | 23 tests: reader validation, writer output, round-trip, data types, header tests.                      |
| **FileFormat.Ktx.Tests** | net8.0          | NUnit 4 | 33 tests: reader validation, writer output, round-trip, KTX1 + KTX2 header tests.                     |
| **FileFormat.Exr.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, round-trip, magic header tests.                            |
| **FileFormat.Dpx.Tests** | net8.0          | NUnit 4 | 32 tests: reader validation, writer output, round-trip, BE/LE endianness, header tests.                |
| **FileFormat.Fits.Tests** | net8.0         | NUnit 4 | 28 tests: reader validation, writer output, header parser, round-trip, data types.                     |
| **FileFormat.Ccitt.Tests** | net8.0        | NUnit 4 | 46 tests: reader validation, writer output, G3/G4 codecs, Huffman tables, round-trip.                  |
| **FileFormat.BbcMicro.Tests** | net8.0     | NUnit 4 | 34 tests: reader validation, writer output, layout converter, round-trip, data types.                  |
| **FileFormat.BioRadPic.Tests** | net8.0    | NUnit 4 | 49 tests: reader validation, writer output, header tests, round-trip (8/16-bit), data types.           |
| **FileFormat.C64Multi.Tests** | net8.0     | NUnit 4 | 26 tests: reader validation, writer output, round-trip (hires + multicolor), data types.               |
| **FileFormat.Psd.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, round-trip, header tests, data types.                      |
| **FileFormat.Ptif.Tests** | net8.0         | NUnit 4 | 47 tests: reader validation, writer output, round-trip (gray/RGB/RGBA/file/stream), data type tests.   |
| **FileFormat.Hrz.Tests** | net8.0          | NUnit 4 | 13 tests: reader validation, writer output, round-trip.                                                |
| **FileFormat.Cmu.Tests** | net8.0          | NUnit 4 | 19 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Mtv.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Qrt.Tests** | net8.0          | NUnit 4 | 17 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Qtif.Tests** | net8.0         | NUnit 4 | 36 tests: reader validation, writer output, round-trip, data type tests.                               |
| **FileFormat.Msp.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, RLE compressor, round-trip, header tests.                  |
| **FileFormat.Dcx.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip, multi-page PCX container.                      |
| **FileFormat.Astc.Tests** | net8.0         | NUnit 4 | 21 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Pkm.Tests** | net8.0          | NUnit 4 | 19 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Tim.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, round-trip, CLUT, header tests.                            |
| **FileFormat.Tim2.Tests** | net8.0         | NUnit 4 | 22 tests: reader validation, writer output, round-trip, multi-picture, header tests.                   |
| **FileFormat.Wal.Tests** | net8.0          | NUnit 4 | 21 tests: reader validation, writer output, round-trip, mipmap, header tests.                          |
| **FileFormat.Pvr.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Wpg.Tests** | net8.0          | NUnit 4 | 24 tests: reader validation, writer output, RLE, round-trip, header tests.                             |
| **FileFormat.Wsq.Tests** | net8.0          | NUnit 4 | 66 tests: reader validation, writer output, wavelet DWT, Huffman coding, quantization, round-trip, data type tests. |
| **FileFormat.Bsave.Tests** | net8.0        | NUnit 4 | 25 tests: reader validation, writer output, round-trip, mode detection, header tests.                  |
| **FileFormat.Clp.Tests** | net8.0          | NUnit 4 | 18 tests: reader validation, writer output, round-trip, header tests.                                  |
| **FileFormat.Spectrum512.Tests** | net8.0  | NUnit 4 | 13 tests: reader validation, writer output, round-trip.                                                |
| **FileFormat.Tiny.Tests** | net8.0         | NUnit 4 | 20 tests: reader validation, writer output, round-trip, compression, header tests.                     |
| **FileFormat.Uhdr.Tests** | net8.0         | NUnit 4 | 40 tests: reader validation, writer output, header tests, round-trip, data type tests.                 |
| **FileFormat.Sixel.Tests** | net8.0        | NUnit 4 | 22 tests: reader validation, writer output, round-trip, encoding tests.                                |
| **FileFormat.Wad.Tests** | net8.0          | NUnit 4 | 29 tests: reader validation, writer output, round-trip, lump management, header tests.                 |
| **FileFormat.Wad3.Tests** | net8.0         | NUnit 4 | 26 tests: reader validation, writer output, round-trip, MipTex, palette, header tests.                 |
| **FileFormat.Apng.Tests** | net8.0         | NUnit 4 | 31 tests: reader validation, writer output, round-trip, acTL/fcTL/fdAT chunks, animation.             |
| **FileFormat.Mng.Tests** | net8.0          | NUnit 4 | 23 tests: reader validation, writer output, round-trip, MHDR chunk, embedded PNG frames.               |
| **FileFormat.Xcf.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, round-trip, tile encoding, RLE/zlib, header tests.         |
| **FileFormat.Pict.Tests** | net8.0         | NUnit 4 | 16 tests: reader validation, writer output, round-trip, PackBits, header tests.                        |
| **FileFormat.Dicom.Tests** | net8.0        | NUnit 4 | 23 tests: reader validation, writer output, round-trip, tag parsing, header tests.                     |
| **FileFormat.DjVu.Tests** | net8.0         | NUnit 4 | 61 tests: reader validation, writer output, chunk parsing, round-trip (RGB24, DPI, via file/RawImage), data types. |
| **FileFormat.Miff.Tests** | net8.0         | NUnit 4 | 30 tests: reader validation, writer output, header parser, RLE compressor, round-trip, data types.      |
| **FileFormat.OpenRaster.Tests** | net8.0  | NUnit 4 | 29 tests: reader validation, writer output, round-trip (layers, position, opacity), data type tests.   |
| **FileFormat.Avs.Tests** | net8.0          | NUnit 4 | 16 tests: reader validation, writer output, round-trip, data type tests.                               |
| **FileFormat.Otb.Tests** | net8.0          | NUnit 4 | 26 tests: reader validation, writer output, round-trip, header tests, data type tests.                 |
| **FileFormat.AliasPix.Tests** | net8.0    | NUnit 4 | 22 tests: reader validation, writer output, RLE compressor, round-trip, header tests.                  |
| **FileFormat.Xwd.Tests** | net8.0          | NUnit 4 | 21 tests: reader validation, writer output, round-trip, header tests, data type tests.                 |
| **FileFormat.ScitexCt.Tests** | net8.0    | NUnit 4 | 20 tests: reader validation, writer output, round-trip (CMYK/RGB/Grayscale), header tests, data types. |
| **FileFormat.Viff.Tests** | net8.0         | NUnit 4 | 34 tests: reader validation, writer output, round-trip (8/16/float/multi-band/map), header tests, data types. |
| **FileFormat.Jbig2.Tests** | net8.0        | NUnit 4 | 55 tests: reader validation, writer output, segment tests, MMR codec round-trip, round-trip (white/black/checkerboard/diagonal/file/RawImage), data types. |
| **FileFormat.Jng.Tests** | net8.0          | NUnit 4 | 28 tests: reader validation, writer output, round-trip (color/gray/alpha/JPEG alpha), header tests, data types. |
| **FileFormat.Rla.Tests** | net8.0          | NUnit 4 | 31 tests: reader validation, writer output, RLE compressor, round-trip (RGB/RGBA/16-bit/single-channel), header tests. |
| **FileFormat.Vicar.Tests** | net8.0        | NUnit 4 | 27 tests: reader validation, writer output, header parser, round-trip (Byte/Half/Real/Doub/MultiBand), data types.     |
| **FileFormat.Nrrd.Tests** | net8.0         | NUnit 4 | 34 tests: reader validation, writer output, header parser, round-trip (UInt8/Float/Gzip/MultiDim/Spacings), data types. |
| **FileFormat.Msx.Tests** | net8.0          | NUnit 4 | 31 tests: reader validation, writer output, round-trip (SC2/SC5/SC8/BLOAD), data type tests.          |
| **FileFormat.Analyze.Tests** | net8.0      | NUnit 4 | 51 tests: reader validation, writer output, round-trip (grayscale/RGB24/via file/via RawImage), data type tests, ToRawImage/FromRawImage. |
| **FileFormat.Nifti.Tests** | net8.0        | NUnit 4 | 29 tests: reader validation, writer output, round-trip (UInt8/Int16/Float32/Rgb24/3D), header tests, data types. |
| **FileFormat.Nitf.Tests** | net8.0         | NUnit 4 | 45 tests: reader validation, writer output, round-trip (grayscale/RGB/via file/RawImage), data type tests. |
| **FileFormat.Sff.Tests** | net8.0          | NUnit 4 | 27 tests: reader validation, writer output, round-trip (single/multi page), header tests, page header tests. |
| **FileFormat.Art.Tests** | net8.0          | NUnit 4 | 23 tests: reader validation, writer output, round-trip (single/multi/empty/anim), header tests, data types.     |
| **FileFormat.Acorn.Tests** | net8.0        | NUnit 4 | 30 tests: reader validation, writer output, round-trip (1/4/8/32bpp, mask, palette, multi-sprite), header tests, data types. |
| **FileFormat.Cals.Tests** | net8.0         | NUnit 4 | 22 tests: reader validation, writer output, header parser, round-trip (200/300dpi, via file, non-byte-aligned), data types. |
| **FileFormat.Oric.Tests** | net8.0          | NUnit 4 | 15 tests: reader validation, writer output, round-trip (all zeros, attributes, mixed data, all ones), data types.           |
| **FileFormat.Miff.Tests** | net8.0         | NUnit 4 | 30 tests: reader validation, writer output, header parser, RLE compressor, round-trip (RGB/RGBA/Palette/Grayscale/RLE), data types. |
| **FileFormat.AppleII.Tests** | net8.0      | NUnit 4 | 21 tests: reader validation, writer output, layout converter, round-trip (HGR/DHGR), data types.   |
| **FileFormat.AppleIIgs.Tests** | net8.0    | NUnit 4 | 25 tests: reader validation, writer output, round-trip (320/640 mode, palette, SCB), data types.    |
| **FileFormat.Palm.Tests** | net8.0         | NUnit 4 | 23 tests: reader validation, writer output, RLE compressor, round-trip (1/4/8/16 bpp, RLE), header tests, data types. |
| **FileFormat.Pcd.Tests** | net8.0          | NUnit 4 | 35 tests: reader validation, writer output, round-trip (RGB24, different sizes, via file, via RawImage), data types.  |
| **FileFormat.Bsb.Tests** | net8.0          | NUnit 4 | 44 tests: reader validation, writer output, round-trip (indexed palette, RLE, via file), data types.                 |
| **FileFormat.Awd.Tests** | net8.0          | NUnit 4 | 32 tests: reader validation, writer output, round-trip (monochrome, via file), data types.                           |
| **FileFormat.Psp.Tests** | net8.0          | NUnit 4 | 40 tests: reader validation, writer output, round-trip (RGB24, via file, via RawImage), data types.                  |
| **FileFormat.PcPaint.Tests** | net8.0      | NUnit 4 | 62 tests: reader validation, writer output, RLE compressor, round-trip (indexed, via file, via RawImage), data types. |
| **FileFormat.PalmPdb.Tests** | net8.0      | NUnit 4 | 37 tests: reader validation, writer output, round-trip (RGB24, via file), data types.                                |
| **FileFormat.PhotoPaint.Tests** | net8.0   | NUnit 4 | 41 tests: reader validation, writer output, round-trip (RGB24, via file), data types.                                |
| **FileFormat.Pdn.Tests** | net8.0          | NUnit 4 | 33 tests: reader validation, writer output, round-trip (BGRA32, gzip compression, via file), data types.             |
| **FileFormat.Fpx.Tests** | net8.0          | NUnit 4 | 36 tests: reader validation, writer output, round-trip (RGB24, via file), data types.                                |
| **FileFormat.SamCoupe.Tests** | net8.0     | NUnit 4 | 24 tests: reader validation, writer output, round-trip (Mode 3/Mode 4), data types.                |
| **FileFormat.Trs80.Tests** | net8.0       | NUnit 4 | 41 tests: reader validation, writer output, round-trip (cells, via file, via RawImage, bit mapping), data types. |
| **FileFormat.IffRgbn.Tests** | net8.0     | NUnit 4 | Tests: reader validation, writer output, round-trip (13-bit RGB, repeat counts), data types. |
| **FileFormat.SnesTile.Tests** | net8.0    | NUnit 4 | Tests: reader validation, writer output, round-trip (4BPP planar tiles, via RawImage), data types. |
| **FileFormat.SegaGenTile.Tests** | net8.0 | NUnit 4 | Tests: reader validation, writer output, round-trip (nibble-packed tiles, via RawImage), data types. |
| **FileFormat.PcEngineTile.Tests** | net8.0 | NUnit 4 | Tests: reader validation, writer output, round-trip (4BPP planar tiles, via RawImage), data types. |
| **FileFormat.MasterSystemTile.Tests** | net8.0 | NUnit 4 | Tests: reader validation, writer output, round-trip (interleaved bitplanes, via RawImage), data types. |
| **FileFormat.SymbianMbm.Tests** | net8.0  | NUnit 4 | Tests: reader validation, writer output, round-trip (multi-bitmap, RLE), data types. |
| **FileFormat.XvThumbnail.Tests** | net8.0 | NUnit 4 | Tests: reader validation, writer output, round-trip (3-3-2 RGB packing), data types. |
| **FileFormat.Mrc.Tests** | net8.0         | NUnit 4 | Tests: reader validation, writer output, round-trip (int8/RGB modes, MAP magic), data types. |
| **FileFormat.Gd2.Tests** | net8.0         | NUnit 4 | Tests: reader validation, writer output, round-trip (truecolor/indexed, chunked), data types. |
| **FileFormat.BigTiff.Tests** | net8.0     | NUnit 4 | 35 tests: reader validation, writer output (byte order/version/offsets), round-trip (Gray8/Rgb24, via file, via RawImage), data types. |
| **FileFormat.AutodeskCel.Tests** | net8.0 | NUnit 4 | Tests: reader validation, writer output, round-trip (indexed 8-bit, VGA palette), data types. |
| **FileFormat.Wad2.Tests** | net8.0        | NUnit 4 | Tests: reader validation, writer output, round-trip (MipTex, mipmaps, Quake palette), data types. |
| **FileFormat.Jpeg2000**  | net8.0          | Library | JPEG 2000 (ISO 15444-1) reader/writer: JP2 container with ISOBMFF-like boxes, J2K codestream (SIZ/COD/QCD markers), LeGall 5/3 reversible DWT, Gray8/Rgb24, lossless round-trip. |
| **FileFormat.Jpeg2000.Tests** | net8.0    | NUnit 4 | 68 tests: reader validation, writer output (JP2 signature/ftyp/jp2h/jp2c/SIZ/EOC), box parser, wavelet 1D/2D/multi-level round-trip, round-trip (grayscale/RGB/gradient/file/RawImage/odd dimensions), data types. |
| **GifFileFormat**        | net8.0-windows  | Library | GIF reader/writer with LZW codec (in [AnythingToGif](https://github.com/Hawkynt/AnythingToGif) repo). |
| **FileFormat.Bmp**       | net8.0          | Library | BMP file format reader/writer with RLE support.                                                        |
| **FileFormat.Tga**       | net8.0          | Library | TGA file format reader/writer with RLE and TGA 2.0 footer support.                                     |
| **FileFormat.Pcx**       | net8.0          | Library | PCX file format reader/writer with RLE encoding.                                                        |
| **FileFormat.Pds**       | net8.0          | Library | NASA Planetary Data System (PDS3) reader/writer: text label header, raw pixel data, BSQ/BIP/BIL band storage. |
| **FileFormat.Jpeg**      | net8.0          | Library | JPEG file format reader/writer (wraps BitMiracle.LibJpeg.NET).                                          |
| **FileFormat.JpegLs**    | net8.0          | Library | JPEG-LS (ITU-T T.87 / LOCO-I) reader/writer: MED prediction, Golomb-Rice coding, run mode, context-based adaptive coding, Gray8/Rgb24. |
| **FileFormat.Jbig**      | net8.0          | Library | JBIG (ITU-T T.82) bi-level image reader/writer: QM arithmetic coding, Template 0 context model, TPBON, 1bpp Indexed1. |
| **FileFormat.JpegXl**    | net8.0          | Library | JPEG XL (ISO/IEC 18181) container reader/writer: ISOBMFF container with ftyp/jxlc boxes, bare codestream (FF 0A), variable-length SizeHeader, Gray8/Rgb24. |
| **FileFormat.JpegXr**    | net8.0          | Library | JPEG XR (ITU-T T.832) reader/writer: TIFF-like container with II byte order, 0xBC01 magic, IFD tags, Gray8/Rgb24. |
| **FileFormat.Heif**      | net8.0          | Library | HEIF/HEIC (ISO/IEC 23008-12) container reader/writer: ISOBMFF ftyp/meta/mdat boxes, heic/heix/hevc/mif1 brands, ispe dimensions, Rgb24 pixel data, raw HEVC payload storage. |
| **FileFormat.Avif**      | net8.0          | Library | AVIF (AV1 Image File Format) container reader/writer: ISOBMFF ftyp/meta/mdat boxes, avif/avis brands, ispe dimensions, Rgb24 pixel data, raw AV1 payload storage. |
| **FileFormat.Bpg**       | net8.0          | Library | BPG (Better Portable Graphics) reader/writer: 42 50 47 FB magic, ue7 variable-length width/height encoding, pixel format and color space enums, Rgb24 pixel data, raw HEVC payload storage. |
| **FileFormat.Dng**       | net8.0          | Library | DNG (Adobe Digital Negative) reader/writer: TIFF/EP based with DNGVersion tag (50706), uncompressed only, Gray8/Rgb24/Rgba32 pixel data, IFD-based metadata. |
| **FileFormat.CameraRaw** | net8.0          | Library | Camera RAW multi-manufacturer reader/writer: CR2/NEF/ARW/ORF/RW2/PEF/RAF/RAW/SRW/DCS extensions, TIFF-based preview extraction, Fujifilm RAF parsing, Rgb24 pixel data. |
| **FileFormat.Krita**     | net8.0          | Library | Krita (.kra) ZIP container reader/writer: mergedimage.png composite extraction via FileFormat.Png. |
| **FileFormat.Analyze**   | net8.0          | Library | Analyze 7.5 (.hdr+.img) medical imaging reader/writer: 348-byte LE header, companion .img file, Gray8/Rgb24. |
| **FileFormat.MetaImage**  | net8.0         | Library | MetaImage (.mha/.mhd) ITK/VTK reader/writer: text tag=value header, embedded or external .raw data, gzip support, Gray8/Rgb24. |
| **FileFormat.Envi**      | net8.0          | Library | ENVI remote sensing image reader/writer: text header with keyword=value lines, multiline brace values, BSQ/BIP/BIL band interleave, Gray8/Rgb24. |
| **FileFormat.Eps**       | net8.0          | Library | EPS reader/writer: DOS EPS Binary Header (C5 D0 D3 C6), TIFF preview extraction. References FileFormat.Tiff. |
| **FileFormat.Wmf**       | net8.0          | Library | WMF (Windows Metafile) reader/writer: Placeable WMF header, META_STRETCHDIB DIB extraction. References FileFormat.Bmp. |
| **FileFormat.Emf**       | net8.0          | Library | EMF (Enhanced Metafile) reader/writer: EMR_HEADER + " EMF" at offset 40, EMR_STRETCHDIBITS DIB extraction. References FileFormat.Bmp. |
| **FileFormat.Vips**      | net8.0          | Library | VIPS (.v, .vips) libvips native format reader/writer: 64-byte header, raw UCHAR pixels, Gray8/Rgb24/Rgba32. |
| **FileFormat.QuakeSpr**  | net8.0          | Library | Quake 1 sprite (.spr) reader/writer: "IDSP" magic, 36-byte header, indexed 8-bit with embedded Quake palette. |
| **FileFormat.NesChr**    | net8.0          | Library | NES CHR (.chr) 2bpp planar tile reader/writer: 16 bytes/tile, 128px wide, Indexed8 with 4-entry grayscale palette. |
| **FileFormat.GameBoyTile** | net8.0        | Library | Game Boy (.2bpp, .cgb) 2bpp interleaved tile reader/writer: 16 bytes/tile, Indexed8 with 4-entry GB green palette. |
| **FileFormat.Atari8Bit** | net8.0          | Library | Atari 8-bit ANTIC mode screen dump reader/writer: GR.7/GR.8/GR.9/GR.15/HIP modes from extension+file size, Indexed8. |
| **FileFormat.AtariFalcon** | net8.0        | Library | Atari Falcon true-color (.ftc) screen dump reader/writer: fixed 320x240, 16-bit RGB565 big-endian, no header. |
| **FileFormat.DuneGraph** | net8.0          | Library | Atari Falcon DuneGraph (.dg1/.dc1) 256-color indexed reader/writer: fixed 320x200, Falcon 4-byte palette, uncompressed or RLE compressed. |
| **FileFormat.PrismPaint** | net8.0         | Library | Atari Falcon Prism Paint (.pnt/.tpi) 256-color indexed reader/writer: variable resolution (up to 640x480), Falcon 4-byte palette, LE header. |
| **FileFormat.Rembrandt** | net8.0          | Library | Atari Falcon Rembrandt (.tcp) true-color reader/writer: variable resolution, 16-bit RGB565 big-endian pixels, BE header. |
| **FileFormat.CokeAtari** | net8.0          | Library | COKE Atari Falcon (.tg1) 16-bit true color reader/writer: variable resolution, 4-byte BE header (width, height), RGB565 pixels. |
| **FileFormat.AtariFalconXga** | net8.0     | Library | Atari Falcon XGA (.xga) 16-bit true color reader/writer: variable resolution, 4-byte BE header (width, height), RGB565 pixels. |
| **FileFormat.SpookySpritesFalcon** | net8.0 | Library | Spooky Sprites Falcon (.tre) compressed 16-bit true color reader/writer: variable resolution, 4-byte BE header, RLE-compressed RGB565 pixels. |
| **FileFormat.IffAnim**   | net8.0          | Library | IFF ANIM (.anim) animation container reader/writer: FORM ANIM wrapping ILBM frames. References FileFormat.Iff/Ilbm. |
| **FileFormat.SoftImage**  | net8.0          | Library | Softimage PIC (.pic) reader/writer: 0x5380F634 magic, 100-byte BE header, mixed RLE compressed RGB/RGBA. |
| **FileFormat.MayaIff**   | net8.0          | Library | Maya IFF (.iff, .maya) reader/writer: FOR4/CIMG container, TBHD header, RGBA/RGB uncompressed pixel data. |
| **FileFormat.Xcursor**   | net8.0          | Library | X11 Xcursor (.xcur, .cursor) reader/writer: "Xcur" magic, TOC-based image chunks, premultiplied ARGB pixels. |
| **FileFormat.Interfile**  | net8.0          | Library | Interfile (.hv) nuclear medicine reader/writer: "!INTERFILE" magic, keyword=value header, companion data file, Gray8/Rgb24. |
| **FileFormat.Mpo**       | net8.0          | Library | MPO (Multi-Picture Object) reader/writer: concatenated JPEGs with APP2 MPF index. References FileFormat.Jpeg. |
| **FileFormat.Trs80**     | net8.0          | Library | TRS-80 hi-res (.hr) screen dump reader/writer: fixed 6144 bytes, 128x48 char cells, 2x3 pixel blocks (256x144). |
| **FileFormat.IffRgbn**   | net8.0          | Library | IFF RGBN (NewTek/Amiga) reader/writer: FORM+RGBN, 13-bit RGB + genlock, 2-byte pixel groups with repeat counts. References FileFormat.Iff. |
| **FileFormat.SnesTile**  | net8.0          | Library | SNES 4BPP planar tile reader/writer: 8x8 tiles, 32 bytes/tile, 4 bitplanes (0+1 interleaved, 2+3 interleaved), 128px wide, Indexed8 with 16-entry grayscale. |
| **FileFormat.SegaGenTile** | net8.0        | Library | Sega Genesis/Mega Drive 4BPP tile reader/writer: 8x8 tiles, 32 bytes/tile, nibble-packed (MSB=left pixel), 128px wide, Indexed8 with 16-entry grayscale. |
| **FileFormat.PcEngineTile** | net8.0       | Library | PC Engine/TurboGrafx-16 4BPP planar tile reader/writer: 8x8 tiles, 32 bytes/tile, SNES-style bitplane layout, 128px wide, Indexed8 with 16-entry grayscale. |
| **FileFormat.MasterSystemTile** | net8.0   | Library | Sega Master System/Game Gear 4BPP planar tile reader/writer: 8x8 tiles, 32 bytes/tile, per-row interleaved bitplanes, 128px wide, Indexed8 with 16-entry grayscale. |
| **FileFormat.SymbianMbm** | net8.0         | Library | Symbian OS multi-bitmap (.mbm) reader/writer: UID1 0x10000037, 40-byte bitmap headers, 1/2/4/8/24bpp, optional RLE compression. |
| **FileFormat.XvThumbnail** | net8.0        | Library | XV image viewer thumbnail reader/writer: "P7 332" text magic, 3-3-2 bit RGB packing (1 byte/pixel), Rgb24 output. |
| **FileFormat.Mrc**       | net8.0          | Library | MRC2014 electron microscopy reader/writer: 1024-byte header, "MAP " magic at offset 208, int8/int16/float32/uint16 modes, Gray8/Rgb24. |
| **FileFormat.Gd2**       | net8.0          | Library | libgd GD2 internal format reader/writer: "gd2\0" magic, chunked/raw truecolor (ARGB BE, 7-bit alpha) or indexed, zlib compression. |
| **FileFormat.BigTiff**   | net8.0          | Library | BigTIFF (64-bit TIFF) reader/writer: II/MM byte order + version 43, 64-bit IFD offsets, 20-byte entries, uncompressed Gray8/Rgb24. |
| **FileFormat.AutodeskCel** | net8.0        | Library | Autodesk Animator CEL reader/writer: 0x9119 LE magic, 16-byte header, 8-bit indexed pixels, optional 768-byte VGA palette. |
| **FileFormat.Wad2**      | net8.0          | Library | Quake 1 WAD2 texture container reader/writer: "WAD2" magic, 32-byte directory entries, MipTex with 4 mipmap levels, external Quake palette. |
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
| `MpoFile`             | FileFormat.Mpo   | In-memory MPO representation: list of JPEG byte arrays. Implements `IImageFileFormat<MpoFile>`.                                                                                                                                                                      |
| `MpoReader`           | FileFormat.Mpo   | Static MPO parser: APP2 MPF marker for image offsets, SOI scan fallback.                                                                                                                                                                                             |
| `MpoWriter`           | FileFormat.Mpo   | MPO assembler: injects APP2 MPF with TIFF-style MP Index IFD, concatenates JPEGs.                                                                                                                                                                                    |
| `TiffReader`          | FileFormat.Tiff  | Static TIFF parser via LibTiff.NET wrapper.                                                                                                                                                                                                                          |
| `TiffWriter`          | FileFormat.Tiff  | TIFF byte stream assembly with custom raw strip/tile writing for Zopfli integration.                                                                                                                                                                                |
| `IcoReader`           | FileFormat.Ico   | Static ICO parser: header, directory entries, auto-detects BMP DIB vs PNG via signature sniffing.                                                                                                                                                                   |
| `IcoWriter`           | FileFormat.Ico   | ICO byte stream assembly: header, directory entries, image data.                                                                                                                                                                                                     |
| `CurReader`           | FileFormat.Cur   | Static CUR parser: reuses IcoReader internals with type=2 validation, extracts hotspot coordinates from directory entries.                                                                                                                                           |
| `CurWriter`           | FileFormat.Cur   | CUR byte stream assembly: reuses IcoWriter internals with type=2 and hotspot field override.                                                                                                                                                                         |
| `CurOptimizer`        | Optimizer.Cur    | Main CUR engine. Per-entry BMP/PNG format selection, 2^n combo generation (capped at 256), hotspot preservation, parallel testing.                                                                                                                                   |
| `AnalyzeReader`       | FileFormat.Analyze | Static Analyze 7.5 parser: validates sizeof_hdr=348, reads dim array for width/height, datatype, bitpix. Supports paired .hdr/.img files and concatenated bytes.                                                                                                   |
| `AnalyzeWriter`       | FileFormat.Analyze | Analyze 7.5 byte stream assembly: 348-byte LE header + raw pixel data.                                                                                                                                                                                              |
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
| `ImageOptimizer`      | Optimizer.Image  | Universal optimization engine: auto-detect format, same-format optimization, cross-format conversion, smallest-wins selection.                                                                                                                                       |
| `FormatRegistry`      | Optimizer.Image  | Reflection-based format registry: auto-discovers `IImageFileFormat<T>` implementations at startup, reads `[FormatMagicBytes]`/`[FormatDetectionPriority]` attributes and `static virtual` interface members for capabilities and signature matching. Provides extension lookup, signature detection, and conversion target enumeration. |
| `ImageFormatDetector` | Optimizer.Image  | Static format detection: delegates magic byte signatures to `FormatRegistry`, extension fallback via registry with TIFF-based raw format overrides (DNG/CameraRaw).                                                                                                   |
| `CrushRunner`         | Crush.Core       | Shared CLI runner: input/output validation, cancellation, progress reporting, stopwatch, savings display.                                                                                                                                                            |
| `OptimizationProgress` | Crush.Core      | Shared progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar`, `Phase`.                                                                                                                                                                                 |

## Build / Test / Run

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Run all tests (all test projects are under Tests/)
dotnet test Tests/Compression.Tests/Compression.Tests.csproj
dotnet test Tests/Optimizer.Png.Tests/Optimizer.Png.Tests.csproj
dotnet test Tests/Optimizer.Gif.Tests/Optimizer.Gif.Tests.csproj
dotnet test Tests/Optimizer.Tiff.Tests/Optimizer.Tiff.Tests.csproj
dotnet test Tests/Optimizer.Bmp.Tests/Optimizer.Bmp.Tests.csproj
dotnet test Tests/Optimizer.Tga.Tests/Optimizer.Tga.Tests.csproj
dotnet test Tests/Optimizer.Pcx.Tests/Optimizer.Pcx.Tests.csproj
dotnet test Tests/Optimizer.Jpeg.Tests/Optimizer.Jpeg.Tests.csproj
dotnet test Tests/Optimizer.Ico.Tests/Optimizer.Ico.Tests.csproj
dotnet test Tests/Optimizer.Cur.Tests/Optimizer.Cur.Tests.csproj
dotnet test Tests/Optimizer.Ani.Tests/Optimizer.Ani.Tests.csproj
dotnet test Tests/Optimizer.WebP.Tests/Optimizer.WebP.Tests.csproj
dotnet test Tests/Optimizer.Image.Tests/Optimizer.Image.Tests.csproj
dotnet test Tests/FileFormat.Png.Tests/FileFormat.Png.Tests.csproj
dotnet test Tests/FileFormat.Bmp.Tests/FileFormat.Bmp.Tests.csproj
dotnet test Tests/FileFormat.Tga.Tests/FileFormat.Tga.Tests.csproj
dotnet test Tests/FileFormat.Pcx.Tests/FileFormat.Pcx.Tests.csproj
dotnet test Tests/FileFormat.Jpeg.Tests/FileFormat.Jpeg.Tests.csproj
dotnet test Tests/FileFormat.Mpo.Tests/FileFormat.Mpo.Tests.csproj
dotnet test Tests/FileFormat.Tiff.Tests/FileFormat.Tiff.Tests.csproj
dotnet test Tests/FileFormat.Ico.Tests/FileFormat.Ico.Tests.csproj
dotnet test Tests/FileFormat.Cur.Tests/FileFormat.Cur.Tests.csproj
dotnet test Tests/FileFormat.Analyze.Tests/FileFormat.Analyze.Tests.csproj
dotnet test Tests/FileFormat.Ani.Tests/FileFormat.Ani.Tests.csproj
dotnet test Tests/FileFormat.WebP.Tests/FileFormat.WebP.Tests.csproj
dotnet test Tests/FileFormat.Riff.Tests/FileFormat.Riff.Tests.csproj
dotnet test Tests/FileFormat.Wbmp.Tests/FileFormat.Wbmp.Tests.csproj
dotnet test Tests/FileFormat.Qoi.Tests/FileFormat.Qoi.Tests.csproj
dotnet test Tests/FileFormat.Farbfeld.Tests/FileFormat.Farbfeld.Tests.csproj
dotnet test Tests/FileFormat.Netpbm.Tests/FileFormat.Netpbm.Tests.csproj
dotnet test Tests/FileFormat.Xbm.Tests/FileFormat.Xbm.Tests.csproj
dotnet test Tests/FileFormat.Xpm.Tests/FileFormat.Xpm.Tests.csproj
dotnet test Tests/FileFormat.MacPaint.Tests/FileFormat.MacPaint.Tests.csproj
dotnet test Tests/FileFormat.ZxSpectrum.Tests/FileFormat.ZxSpectrum.Tests.csproj
dotnet test Tests/FileFormat.Koala.Tests/FileFormat.Koala.Tests.csproj
dotnet test Tests/FileFormat.Degas.Tests/FileFormat.Degas.Tests.csproj
dotnet test Tests/FileFormat.CrackArt.Tests/FileFormat.CrackArt.Tests.csproj
dotnet test Tests/FileFormat.Neochrome.Tests/FileFormat.Neochrome.Tests.csproj
dotnet test Tests/FileFormat.GemImg.Tests/FileFormat.GemImg.Tests.csproj
dotnet test Tests/FileFormat.AmstradCpc.Tests/FileFormat.AmstradCpc.Tests.csproj
dotnet test Tests/FileFormat.Pfm.Tests/FileFormat.Pfm.Tests.csproj
dotnet test Tests/FileFormat.Sgi.Tests/FileFormat.Sgi.Tests.csproj
dotnet test Tests/FileFormat.SunRaster.Tests/FileFormat.SunRaster.Tests.csproj
dotnet test Tests/FileFormat.Hdr.Tests/FileFormat.Hdr.Tests.csproj
dotnet test Tests/FileFormat.UtahRle.Tests/FileFormat.UtahRle.Tests.csproj
dotnet test Tests/FileFormat.DrHalo.Tests/FileFormat.DrHalo.Tests.csproj
dotnet test Tests/FileFormat.Iff.Tests/FileFormat.Iff.Tests.csproj
dotnet test Tests/FileFormat.Ilbm.Tests/FileFormat.Ilbm.Tests.csproj
dotnet test Tests/FileFormat.Ingr.Tests/FileFormat.Ingr.Tests.csproj
dotnet test Tests/FileFormat.Jbig2.Tests/FileFormat.Jbig2.Tests.csproj
dotnet test Tests/FileFormat.Fli.Tests/FileFormat.Fli.Tests.csproj
dotnet test Tests/FileFormat.Flif.Tests/FileFormat.Flif.Tests.csproj
dotnet test Tests/FileFormat.Fsh.Tests/FileFormat.Fsh.Tests.csproj
dotnet test Tests/FileFormat.Cineon.Tests/FileFormat.Cineon.Tests.csproj
dotnet test Tests/FileFormat.Dds.Tests/FileFormat.Dds.Tests.csproj
dotnet test Tests/FileFormat.Vtf.Tests/FileFormat.Vtf.Tests.csproj
dotnet test Tests/FileFormat.Ktx.Tests/FileFormat.Ktx.Tests.csproj
dotnet test Tests/FileFormat.Exr.Tests/FileFormat.Exr.Tests.csproj
dotnet test Tests/FileFormat.Dpx.Tests/FileFormat.Dpx.Tests.csproj
dotnet test Tests/FileFormat.Fits.Tests/FileFormat.Fits.Tests.csproj
dotnet test Tests/FileFormat.Ccitt.Tests/FileFormat.Ccitt.Tests.csproj
dotnet test Tests/FileFormat.BbcMicro.Tests/FileFormat.BbcMicro.Tests.csproj
dotnet test Tests/FileFormat.BioRadPic.Tests/FileFormat.BioRadPic.Tests.csproj
dotnet test Tests/FileFormat.C64Multi.Tests/FileFormat.C64Multi.Tests.csproj
dotnet test Tests/FileFormat.Psd.Tests/FileFormat.Psd.Tests.csproj
dotnet test Tests/FileFormat.Ptif.Tests/FileFormat.Ptif.Tests.csproj
dotnet test Tests/FileFormat.Hrz.Tests/FileFormat.Hrz.Tests.csproj
dotnet test Tests/FileFormat.Cmu.Tests/FileFormat.Cmu.Tests.csproj
dotnet test Tests/FileFormat.Mtv.Tests/FileFormat.Mtv.Tests.csproj
dotnet test Tests/FileFormat.Qrt.Tests/FileFormat.Qrt.Tests.csproj
dotnet test Tests/FileFormat.Qtif.Tests/FileFormat.Qtif.Tests.csproj
dotnet test Tests/FileFormat.Msp.Tests/FileFormat.Msp.Tests.csproj
dotnet test Tests/FileFormat.Dcx.Tests/FileFormat.Dcx.Tests.csproj
dotnet test Tests/FileFormat.Astc.Tests/FileFormat.Astc.Tests.csproj
dotnet test Tests/FileFormat.Pkm.Tests/FileFormat.Pkm.Tests.csproj
dotnet test Tests/FileFormat.Tim.Tests/FileFormat.Tim.Tests.csproj
dotnet test Tests/FileFormat.Tim2.Tests/FileFormat.Tim2.Tests.csproj
dotnet test Tests/FileFormat.Wal.Tests/FileFormat.Wal.Tests.csproj
dotnet test Tests/FileFormat.Pvr.Tests/FileFormat.Pvr.Tests.csproj
dotnet test Tests/FileFormat.Wpg.Tests/FileFormat.Wpg.Tests.csproj
dotnet test Tests/FileFormat.Wsq.Tests/FileFormat.Wsq.Tests.csproj
dotnet test Tests/FileFormat.Bsave.Tests/FileFormat.Bsave.Tests.csproj
dotnet test Tests/FileFormat.Clp.Tests/FileFormat.Clp.Tests.csproj
dotnet test Tests/FileFormat.Spectrum512.Tests/FileFormat.Spectrum512.Tests.csproj
dotnet test Tests/FileFormat.Tiny.Tests/FileFormat.Tiny.Tests.csproj
dotnet test Tests/FileFormat.Uhdr.Tests/FileFormat.Uhdr.Tests.csproj
dotnet test Tests/FileFormat.Sixel.Tests/FileFormat.Sixel.Tests.csproj
dotnet test Tests/FileFormat.Wad.Tests/FileFormat.Wad.Tests.csproj
dotnet test Tests/FileFormat.Wad3.Tests/FileFormat.Wad3.Tests.csproj
dotnet test Tests/FileFormat.Apng.Tests/FileFormat.Apng.Tests.csproj
dotnet test Tests/FileFormat.Mng.Tests/FileFormat.Mng.Tests.csproj
dotnet test Tests/FileFormat.Xcf.Tests/FileFormat.Xcf.Tests.csproj
dotnet test Tests/FileFormat.Pict.Tests/FileFormat.Pict.Tests.csproj
dotnet test Tests/FileFormat.Dicom.Tests/FileFormat.Dicom.Tests.csproj
dotnet test Tests/FileFormat.DjVu.Tests/FileFormat.DjVu.Tests.csproj
dotnet test Tests/FileFormat.Miff.Tests/FileFormat.Miff.Tests.csproj
dotnet test Tests/FileFormat.OpenRaster.Tests/FileFormat.OpenRaster.Tests.csproj
dotnet test Tests/FileFormat.Avs.Tests/FileFormat.Avs.Tests.csproj
dotnet test Tests/FileFormat.Otb.Tests/FileFormat.Otb.Tests.csproj
dotnet test Tests/FileFormat.AliasPix.Tests/FileFormat.AliasPix.Tests.csproj
dotnet test Tests/FileFormat.Xwd.Tests/FileFormat.Xwd.Tests.csproj
dotnet test Tests/FileFormat.ScitexCt.Tests/FileFormat.ScitexCt.Tests.csproj
dotnet test Tests/FileFormat.Jng.Tests/FileFormat.Jng.Tests.csproj
dotnet test Tests/FileFormat.Viff.Tests/FileFormat.Viff.Tests.csproj
dotnet test Tests/FileFormat.Rla.Tests/FileFormat.Rla.Tests.csproj
dotnet test Tests/FileFormat.Nifti.Tests/FileFormat.Nifti.Tests.csproj
dotnet test Tests/FileFormat.Nitf.Tests/FileFormat.Nitf.Tests.csproj
dotnet test Tests/FileFormat.Art.Tests/FileFormat.Art.Tests.csproj
dotnet test Tests/FileFormat.Sff.Tests/FileFormat.Sff.Tests.csproj
dotnet test Tests/FileFormat.Acorn.Tests/FileFormat.Acorn.Tests.csproj
dotnet test Tests/FileFormat.Oric.Tests/FileFormat.Oric.Tests.csproj
dotnet test Tests/FileFormat.Vicar.Tests/FileFormat.Vicar.Tests.csproj
dotnet test Tests/FileFormat.Nrrd.Tests/FileFormat.Nrrd.Tests.csproj
dotnet test Tests/FileFormat.AppleII.Tests/FileFormat.AppleII.Tests.csproj
dotnet test Tests/FileFormat.AppleIIgs.Tests/FileFormat.AppleIIgs.Tests.csproj
dotnet test Tests/FileFormat.Palm.Tests/FileFormat.Palm.Tests.csproj
dotnet test Tests/FileFormat.Pcd.Tests/FileFormat.Pcd.Tests.csproj
dotnet test Tests/FileFormat.SamCoupe.Tests/FileFormat.SamCoupe.Tests.csproj
dotnet test Tests/FileFormat.JpegLs.Tests/FileFormat.JpegLs.Tests.csproj
dotnet test Tests/FileFormat.Jbig.Tests/FileFormat.Jbig.Tests.csproj
dotnet test Tests/FileFormat.Wsq.Tests/FileFormat.Wsq.Tests.csproj
dotnet test Tests/FileFormat.DjVu.Tests/FileFormat.DjVu.Tests.csproj
dotnet test Tests/FileFormat.Jbig2.Tests/FileFormat.Jbig2.Tests.csproj
dotnet test Tests/FileFormat.Flif.Tests/FileFormat.Flif.Tests.csproj
dotnet test Tests/FileFormat.Jpeg2000.Tests/FileFormat.Jpeg2000.Tests.csproj
dotnet test Tests/FileFormat.JpegXl.Tests/FileFormat.JpegXl.Tests.csproj
dotnet test Tests/FileFormat.JpegXr.Tests/FileFormat.JpegXr.Tests.csproj
dotnet test Tests/FileFormat.Heif.Tests/FileFormat.Heif.Tests.csproj
dotnet test Tests/FileFormat.Avif.Tests/FileFormat.Avif.Tests.csproj
dotnet test Tests/FileFormat.Bpg.Tests/FileFormat.Bpg.Tests.csproj
dotnet test Tests/FileFormat.Dng.Tests/FileFormat.Dng.Tests.csproj
dotnet test Tests/FileFormat.CameraRaw.Tests/FileFormat.CameraRaw.Tests.csproj
dotnet test Tests/FileFormat.Krita.Tests/FileFormat.Krita.Tests.csproj
dotnet test Tests/FileFormat.Analyze.Tests/FileFormat.Analyze.Tests.csproj
dotnet test Tests/FileFormat.MetaImage.Tests/FileFormat.MetaImage.Tests.csproj
dotnet test Tests/FileFormat.Envi.Tests/FileFormat.Envi.Tests.csproj
dotnet test Tests/FileFormat.Eps.Tests/FileFormat.Eps.Tests.csproj
dotnet test Tests/FileFormat.Wmf.Tests/FileFormat.Wmf.Tests.csproj
dotnet test Tests/FileFormat.Emf.Tests/FileFormat.Emf.Tests.csproj
dotnet test Tests/FileFormat.Vips.Tests/FileFormat.Vips.Tests.csproj
dotnet test Tests/FileFormat.QuakeSpr.Tests/FileFormat.QuakeSpr.Tests.csproj
dotnet test Tests/FileFormat.NesChr.Tests/FileFormat.NesChr.Tests.csproj
dotnet test Tests/FileFormat.GameBoyTile.Tests/FileFormat.GameBoyTile.Tests.csproj
dotnet test Tests/FileFormat.Atari8Bit.Tests/FileFormat.Atari8Bit.Tests.csproj
dotnet test Tests/FileFormat.AtariFalcon.Tests/FileFormat.AtariFalcon.Tests.csproj
dotnet test Tests/FileFormat.IffAnim.Tests/FileFormat.IffAnim.Tests.csproj
dotnet test Tests/FileFormat.SoftImage.Tests/FileFormat.SoftImage.Tests.csproj
dotnet test Tests/FileFormat.MayaIff.Tests/FileFormat.MayaIff.Tests.csproj
dotnet test Tests/FileFormat.Xcursor.Tests/FileFormat.Xcursor.Tests.csproj
dotnet test Tests/FileFormat.IffPbm.Tests/FileFormat.IffPbm.Tests.csproj
dotnet test Tests/FileFormat.IffAcbm.Tests/FileFormat.IffAcbm.Tests.csproj
dotnet test Tests/FileFormat.IffDeep.Tests/FileFormat.IffDeep.Tests.csproj
dotnet test Tests/FileFormat.IffRgb8.Tests/FileFormat.IffRgb8.Tests.csproj
dotnet test Tests/FileFormat.Interfile.Tests/FileFormat.Interfile.Tests.csproj
dotnet test Tests/FileFormat.Trs80.Tests/FileFormat.Trs80.Tests.csproj
dotnet test Tests/FileFormat.IffRgbn.Tests/FileFormat.IffRgbn.Tests.csproj
dotnet test Tests/FileFormat.SnesTile.Tests/FileFormat.SnesTile.Tests.csproj
dotnet test Tests/FileFormat.SegaGenTile.Tests/FileFormat.SegaGenTile.Tests.csproj
dotnet test Tests/FileFormat.PcEngineTile.Tests/FileFormat.PcEngineTile.Tests.csproj
dotnet test Tests/FileFormat.MasterSystemTile.Tests/FileFormat.MasterSystemTile.Tests.csproj
dotnet test Tests/FileFormat.SymbianMbm.Tests/FileFormat.SymbianMbm.Tests.csproj
dotnet test Tests/FileFormat.XvThumbnail.Tests/FileFormat.XvThumbnail.Tests.csproj
dotnet test Tests/FileFormat.Mrc.Tests/FileFormat.Mrc.Tests.csproj
dotnet test Tests/FileFormat.Gd2.Tests/FileFormat.Gd2.Tests.csproj
dotnet test Tests/FileFormat.BigTiff.Tests/FileFormat.BigTiff.Tests.csproj
dotnet test Tests/FileFormat.AutodeskCel.Tests/FileFormat.AutodeskCel.Tests.csproj
dotnet test Tests/FileFormat.Wad2.Tests/FileFormat.Wad2.Tests.csproj

# Run the unified CLI
dotnet run --project Crush.Image -- auto -i <input.png> -o <output.png>
dotnet run --project Crush.Image -- png -i <input.png> -o <output.png>
dotnet run --project Crush.Image -- gif -i <input.gif> -o <output.gif>
dotnet run --project Crush.Image -- tiff -i <input.tiff> -o <output.tiff>
dotnet run --project Crush.Image -- bmp -i <input.bmp> -o <output.bmp>
dotnet run --project Crush.Image -- tga -i <input.tga> -o <output.tga>
dotnet run --project Crush.Image -- pcx -i <input.pcx> -o <output.pcx>
dotnet run --project Crush.Image -- jpeg -i <input.jpg> -o <output.jpg>
dotnet run --project Crush.Image -- ico -i <input.ico> -o <output.ico>
dotnet run --project Crush.Image -- cur -i <input.cur> -o <output.cur>
dotnet run --project Crush.Image -- ani -i <input.ani> -o <output.ani>
dotnet run --project Crush.Image -- webp -i <input.webp> -o <output.webp>
```

## CLI Reference

`Crush.Image` is a single unified CLI that replaces the 11 format-specific tools. It uses verb-based dispatch:

```bash
crush auto -i input.png -o output.png    # auto-detect, try all formats
crush png -i input.png -o output.png     # PNG-specific options
crush jpeg -i input.jpg -o out.jpg       # JPEG-specific options
crush gif -i input.gif -o output.gif     # GIF-specific options
# ... (png, gif, tiff, bmp, tga, pcx, jpeg, ico, cur, ani, webp)
```

No verb (default) = same as `auto`.

### Common Options (all verbs)

| Option      | Short | Default         | Description                    |
| ----------- | ----- | --------------- | ------------------------------ |
| `--input`   | `-i`  | *(required)*    | Input image file path          |
| `--output`  | `-o`  | *(required)*    | Output image file path         |
| `--jobs`    | `-j`  | `0` (all cores) | Max parallel tasks             |
| `--verbose` | `-v`  | `false`         | Verbose output                 |

### auto (default verb)

| Option             | Short | Default | Description                                       |
| ------------------ | ----- | ------- | ------------------------------------------------- |
| `--convert`        |       | `true`  | Allow cross-format conversion to find smallest     |
| `--lossy`          |       | `false` | Allow lossy compression (JPEG, GIF quantization)   |
| `--strip`          |       | `false` | Strip metadata                                     |
| `--auto-extension` |       | `true`  | Auto-change output extension on format conversion  |

### png

| Option              | Short | Default                                            | Description                                            |
| ------------------- | ----- | -------------------------------------------------- | ------------------------------------------------------ |
| `--convert`         |       | `true`                                             | Allow cross-format conversion                          |
| `--lossy`           |       | `false`                                            | Allow lossy compression                                |
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

### gif

| Option                  | Short | Default                                                | Description                                   |
| ----------------------- | ----- | ------------------------------------------------------ | --------------------------------------------- |
| `--convert`             |       | `true`                                                 | Allow cross-format conversion                 |
| `--lossy`               |       | `false`                                                | Allow lossy compression                       |
| `--strategies`          | `-s`  | `Original,FrequencySorted,LuminanceSorted,LzwRunAware` | Palette reorder strategies                    |
| `--optimize-disposal`   |       | `true`                                                 | Optimize frame disposal methods               |
| `--trim-margins`        |       | `true`                                                 | Trim transparent margins                      |
| `--deferred-clear`      |       | `true`                                                 | Try deferred LZW clear codes                  |
| `--frame-diff`          |       | `true`                                                 | Try frame differencing                        |
| `--deduplicate`         |       | `true`                                                 | Merge identical consecutive frames            |
| `--compression-palette` |       | `false`                                                | Compression-aware palette reordering (slower) |

### tiff

| Option              | Short | Default                                  | Description                               |
| ------------------- | ----- | ---------------------------------------- | ----------------------------------------- |
| `--convert`         |       | `true`                                   | Allow cross-format conversion             |
| `--lossy`           |       | `false`                                  | Allow lossy compression                   |
| `--compression`     | `-c`  | `None,PackBits,Lzw,Deflate,DeflateUltra` | Compression methods                       |
| `--predictor`       |       | `None,HorizontalDifferencing`            | Predictors to try                         |
| `--auto-color-mode` | `-a`  | `true`                                   | Auto-detect optimal color mode            |
| `--dynamic-strips`  |       | `true`                                   | Dynamic strip sizes based on image height |
| `--tiles`           |       | `false`                                  | Try tiled TIFF encoding                   |
| `--tile-sizes`      |       | `64,128,256`                             | Tile sizes (multiples of 16)              |

### bmp

| Option              | Short | Default            | Description                    |
| ------------------- | ----- | ------------------ | ------------------------------ |
| `--convert`         |       | `true`             | Allow cross-format conversion  |
| `--lossy`           |       | `false`            | Allow lossy compression        |
| `--compression`     | `-c`  | `None,Rle8,Rle4`   | Compression methods to try     |
| `--auto-color-mode` | `-a`  | `true`             | Auto-select best color mode    |

### tga

| Option              | Short | Default         | Description                    |
| ------------------- | ----- | --------------- | ------------------------------ |
| `--convert`         |       | `true`          | Allow cross-format conversion  |
| `--lossy`           |       | `false`         | Allow lossy compression        |
| `--compression`     | `-c`  | `None,Rle`      | Compression methods to try     |
| `--auto-color-mode` | `-a`  | `true`          | Auto-select best color mode    |

### pcx

| Option              | Short | Default         | Description                    |
| ------------------- | ----- | --------------- | ------------------------------ |
| `--convert`         |       | `true`          | Allow cross-format conversion  |
| `--lossy`           |       | `false`         | Allow lossy compression        |
| `--auto-color-mode` | `-a`  | `true`          | Auto-select best color mode    |

### jpeg (alias: jpg)

| Option          | Short | Default              | Description                              |
| --------------- | ----- | -------------------- | ---------------------------------------- |
| `--convert`     |       | `true`               | Allow cross-format conversion            |
| `--lossy`       |       | `false`              | Enable lossy re-encoding mode            |
| `--min-quality` | `-q`  | `75`                 | Minimum quality for lossy mode (1-100)   |
| `--qualities`   |       | `75,80,85,90,95`     | Quality levels to try in lossy mode      |
| `--strip`       |       | `true`               | Try stripping metadata (EXIF, ICC, etc.) |

### ico / cur / ani

| Option      | Short | Default         | Description                    |
| ----------- | ----- | --------------- | ------------------------------ |
| `--input`   | `-i`  | *(required)*    | Input file path                |
| `--output`  | `-o`  | *(required)*    | Output file path               |
| `--jobs`    | `-j`  | `0` (all cores) | Max parallel tasks             |
| `--verbose` | `-v`  | `false`         | Verbose output                 |

### webp

| Option             | Short | Default | Description                  |
| ------------------ | ----- | ------- | ---------------------------- |
| `--strip-metadata` | `-s`  | `true`  | Strip metadata (EXIF, ICCP, XMP) |

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
- ~~24 new FileFormat libraries (AVS through Acorn) for professional, scientific, retro, and game formats (Wave 3)~~ (done)
- ~~Unified Crush.Image CLI with auto-detect, cross-format conversion, and verb-based dispatch~~ (done)
- ~~Test project restructuring into Tests/ directory~~ (done)
- ~~Solution folder reorganization (FileFormats/, Tests/, Optimizers/, Image/ groupings in slnx)~~ (done)
- ~~Cross-format bitmap loading via native FileFormat readers (TGA, PCX, QOI, Farbfeld, BMP)~~ (done)
- ~~Attribute-based header field maps (`[HeaderField]`/`[HeaderFiller]` + `HeaderFieldMapper` replacing manual boilerplate across all 72+ headers)~~ (done)
- ~~Source generator binary serializer for auto-generated ReadFrom/WriteTo methods (Phase 1: foundation + pilot)~~ (done)
- ~~Platform-independent RawImage pixel model and PixelConverter with 21 conversion methods~~ (done)
- ~~SIMD-accelerated pixel format converters (Vector128/Vector256 shuffle for RGBA/BGRA/ARGB swaps, 3-to-4/4-to-3 compaction, Gray8 broadcast, band-sequential/interleaved transpose)~~ (done)
- ~~IBinarySerializable&lt;T&gt; generic serializer interface + HeaderSerializer Read&lt;T&gt;/Write&lt;T&gt;/SizeOf&lt;T&gt; API~~ (done)
- ~~Generator bitfield write support (grouped by container offset, combined OR expression)~~ (done)
- ~~Generator gap detection with automatic WriteTo buffer clearing for padded headers~~ (done)
- ~~6 additional header migrations: MspHeader, PvrHeader, ApngActl, CineonHeader, Vp8XHeader, VtfHeader~~ (done)
- Source generator batch migration for remaining ~63 headers
- Embedded sub-struct support in generator (DdsHeader, RIFF, IFF)
- Computed runtime endianness in generator (ViffHeader, KtxHeader)
- Init-only property generation (NiftiHeader)
- ~~RawImage cross-format conversion pipeline (ToRawImage/FromRawImage on all 94 formats, universal BitmapConverter with 86+ format dispatch, SIMD alpha detection, FrameworkExtensions quantization/dithering, 62+ writable conversion targets)~~ (done)
- ~~14 new FileFormat libraries (AAI, RGF, FBM, GBR, PAT, XYZ, LSS16, ColoRIX, SunIcon, CEL, AmigaIcon, GAF, GunPaint, GeoPaint) for trivial/retro formats (Wave 4)~~ (done)
- ~~9 new FileFormat libraries (PSB, ICNS, BLP, FSH, MPO, PDS, ICS, BioRadPic, PTIF) for extensions and containers (Wave 5)~~ (done)
- ~~12 new FileFormat libraries (BSB, AWD, PSP, QTIF, INGR, NITF, UHDR, PalmPdb, PCD, PhotoPaint, PDN, FPX) for medium-complexity formats (Wave 6)~~ (done)
- ~~8 new FileFormat libraries (JPEG-LS, JBIG, WSQ, DjVu, JBIG2, FLIF, JPEG 2000, JPEG XR) for complex codec formats (Wave 7)~~ (done)
- ~~5 new FileFormat libraries (HEIF, AVIF, BPG, DNG, CameraRaw) for container and professional formats (Wave 8)~~ (done)
- ~~12 new FileFormat libraries (Krita, Analyze, MetaImage, EPS, WMF, EMF, VIPS, QuakeSpr, NesChr, GameBoyTile, Atari8Bit, IffAnim) for mixed format coverage (Wave 9)~~ (done)
- ~~12 new FileFormat libraries (SoftImage, MayaIff, ENVI, Xcursor, IffPbm, PcPaint, IffAcbm, IffDeep, IffRgb8, Interfile, AtariFalcon, TRS-80) for IFF variants, professional 3D, scientific, and retro formats (Wave 10)~~ (done)
- ~~12 new FileFormat libraries (SnesTile, SegaGenTile, PcEngineTile, MasterSystemTile, SymbianMbm, XvThumbnail, IffRgbn, Mrc, Gd2, BigTiff, AutodeskCel, Wad2) for console tiles, containers, scientific, and retro formats (Wave 11)~~ (done)
- ~~5 new FileFormat libraries (GbaTile, WonderSwanTile, NeoGeoSprite, NdsTexture, VirtualBoyTile) for game console tile formats (Wave 12)~~ (done)
- ~~8 new FileFormat libraries (Vic20, Dragon, JupiterAce, Zx81, C128, C16Plus4, Electronika, Vector06c) for additional retro home computers (Wave 13)~~ (done)
- ~~5 new FileFormat libraries (TiBitmap, HpGrob, EpaBios, CiscoIp, PocketPc2bp) for calculator and embedded devices (Wave 14)~~ (done)
- ~~3 new FileFormat libraries (Mag, Pi, Q0) for Japanese image formats (Wave 15)~~ (done)
- ~~4 new FileFormat libraries (NokiaLogo, NokiaNlm, SiemensBmx, PsionPic) for phone/mobile formats (Wave 16)~~ (done)
- ~~4 new FileFormat libraries (KofaxKfx, BrooktroutFax, WinFax, EdmicsC4) for fax and document formats (Wave 17)~~ (done)
- ~~4 new FileFormat libraries (PixarRib, Sdt, MatLab, Ipl) for professional/3D formats (Wave 18)~~ (done)
- ~~4 new FileFormat libraries (Vivid, Bob, GfaRaytrace, Cloe) for raytracer and 3D rendering formats (Wave 19)~~ (done)
- ~~4 new FileFormat libraries (QuakeLmp, HalfLifeMdl, HereticM8, DoomFlat) for game/engine texture formats (Wave 20)~~ (done)
- ~~4 new FileFormat libraries (FaxG3, StelaRaw, MonoMagic, Ecw) for miscellaneous gap formats (Wave 21)~~ (done)
- ~~10 new FileFormat libraries (Thomson, CommodorePet, FmTowns, Pc88, Enterprise128, Atari7800, SharpX68k, RiscOsSprite, NeoGeoPocket, Atari2600) for retro, game console, and niche formats (Wave 22)~~ (done)
- ~~14 new FileFormat libraries (Doodle, DoodleComp, MicroIllustrator, Vidcom64, Picasso64, InterPaintHi, InterPaintMc, AdvancedArtStudio, RunPaint, Bfli, FunPainter, DrazPaint, GigaPaint, PrintfoxPagefox) for C64 art program formats (Wave 23)~~ (done)
- ~~11 new FileFormat libraries (Stad, PortfolioGraphics, PrintShop, IconLibrary, YuvRaw, ZeissLsm, Ioca, SpotImage, ImageSystem, ZeissBivas, PrintMaster) for retro/scientific/professional formats (Wave 24)~~ (done)
- ~~9 new FileFormat libraries (Artist64, FacePainter, FunGraphicsMachine, GoDot4Bit, HiresC64, EggPaint, CDUPaint, RainbowPainter, KoalaCompressed) for C64 art programs round 2 (Wave 25)~~ (done)
- ~~8 new FileFormat libraries (DoodleAtari, Spectrum512Comp, Spectrum512Smoosh, DaliRaw, Gigacad, MegaPaint, FontasyGrafik, ArtDirector) for Atari ST round 2 (Wave 26)~~ (done)
- ~~8 new FileFormat libraries (AndrewToolkit, MgrBitmap, FaceServer, DbwRender, SbigCcd, Cp8Gray, ComputerEyes, PmBitmap) for Unix/raytracer/scientific formats (Wave 27)~~ (done)
- ~~8 new FileFormat libraries (DivGameMap, HomeworldLif, Ps2Txc, HayesJtfax, GammaFax, CompW, DolphinEd, NokiaGroupGraphics) for game/fax/misc formats (Wave 28)~~ (done)
- ~~5 new FileFormat libraries (Im5Visilog, Pco16Bit, HiEddi, PaintMagic, SaracenPaint) for additional misc formats (Wave 29)~~ (done)
- ~~Extension aliases for already-supported formats (.icb/.vda/.vst/.bpx for TGA, .dib/.bga/.rl4/.rl8 for BMP, .iris for SGI, .cin for Cineon, .rad for HDR, .jps for JPEG, .pcc/.fcx for PCX, .urt for UtahRle, .dis for Vivid)~~ (done)
- ~~10 new FileFormat libraries (AttGroup4, AccessFax, AdTechFax, BfxBitware, BrotherFax, CanonNavFax, EverexFax, FaxMan, FremontFax, ImagingFax) for fax variant formats batch 1 (Wave 30)~~ (done)
- ~~10 new FileFormat libraries (MobileFax, OazFax, OlicomFax, RicohFax, SciFax, SmartFax, Tg4, TeliFax, VentaFax, WorldportFax) for fax variant formats batch 2 (Wave 31)~~ (done)
- ~~8 new FileFormat libraries (NistIHead, AvhrrImage, ByuSir, Grs16, CsvImage, HfImage, LucasFilm, QuantelVpb) for scientific/satellite/industrial formats (Wave 32)~~ (done)
- ~~8 new FileFormat libraries (RedStormRsb, SegaSj1, SonyMavica, Pic2, FunPhotor, AtariGrafik, Calamus, NewsRoom) for game/camera/retro formats (Wave 33)~~ (done)
- ~~7 new FileFormat libraries (AdexImage, AimGrayScale, QdvImage, SifImage, WebShots, Rlc2, SeqImage) for miscellaneous remaining formats (Wave 34)~~ (done)
- ~~40+ extension aliases for XnView/IrfanView format parity (.jfif/.jpe/.thm for JPEG, .rlb/.rpf for RLA, .pntg/.macp for MacPaint, .rgba/.bw for SGI, .j2k/.jpc for JPEG 2000, .hdp/.wdp for JPEG XR, etc.)~~ (done)
- ~~Decentralized format metadata via reflection-based registry (FormatRegistry auto-discovers IImageFileFormat&lt;T&gt; at startup, FormatMagicBytes/FormatDetectionPriority attributes, static virtual Capabilities/MatchesSignature, ImageFormatDetector delegates to registry — adding a new format no longer requires touching centralized files)~~ (done)
- ~~Read-once pipeline: ImageOptimizer reads file bytes once and reuses for detection + loading (eliminates 2 redundant file reads). Added `FromBytes`/`FromStream` as `static virtual` interface members on `IImageFileFormat<T>` with backward-compatible defaults, wired on 445 format files. `FormatEntry.LoadRawImageFromBytes` enables byte-based loading without temp files.~~ (done)
- ~~Batch reader/writer/file allocation optimization: Replaced 613 `(byte[])array.Clone()` with `array[..]` range operators, replaced 1,223 `Array.Copy` calls with `Span<T>`-based `AsSpan().CopyTo()` across all 448 FileFormat libraries (Readers, Writers, File classes). Added seekable stream fast path to all 448 `FromStream` methods (avoids MemoryStream intermediate copy for seekable streams).~~ (done)
- ~~Multi-image infrastructure (`IMultiImageFileFormat<T>` interface with `ImageCount`, indexed `ToRawImage`) across ICO, CUR, ANI, APNG, MNG, FLI, DCX, MPO, ICNS~~ (done)
- ~~GPU block decoders (BC1-7, ETC1/2, ASTC, PVRTC) for DDS/VTF/KTX/ASTC/PKM/PVR~~ (done)
- ~~Multi-page TIFF/BigTIFF support (follow IFD chains, `Pages` collection, indexed page access)~~ (done)
- ~~WebP VP8L lossless codec (LZ77 + Huffman + transforms) and VP8 lossy codec (boolean arithmetic, IDCT, intra prediction, loop filter)~~ (done)
- ~~FileFormat.Pdf for embedded image extraction (FlateDecode, DCTDecode, CCITTFaxDecode, ASCII85Decode, ASCIIHexDecode stream filters)~~ (done)
- ~~FileFormat.WindowsPe for icon/cursor/bitmap/image resource extraction from EXE/DLL files~~ (done)
- ~~Camera RAW Bayer demosaicing (bilinear + AHD, black level subtraction, white balance, camera-to-sRGB color matrix)~~ (done)
- ~~Pure C# pixel codecs: JPEG 2000 (MQ arithmetic decoder, Tier-1/Tier-2, DWT 5/3+9/7), JPEG XL (rANS entropy, modular mode, squeeze transform, weighted predictor), JPEG-LS (Golomb-Rice, LOCO-I predictor, near-lossless), JPEG XR (adaptive Huffman, 4x4 PCT/overlap, frequency bands), HEIF/HEVC (CABAC, intra prediction, DCT/DST), AVIF/AV1 (CDF-based ANS, intra prediction, Wiener filter), BPG (HEVC-based, YCbCr 4:2:0), FLIF (MANIAC entropy, interlaced NI), JBIG2 (arithmetic MQ, Halftone/Generic/Refinement), DjVu (IW44 wavelet, CDF 9/7 lifting), ECW (range coder, DWT, zerotree)~~ (done)
- Source generator batch migration for remaining ~63 headers (embedded sub-struct, computed endianness, init-only properties)
- Native C# TIFF/JPEG codec implementations (replace BitMiracle dependencies)
- New format libraries: PHM, FL32, FaceSaver, NIE/NIA/NII, Basis Universal, CDXL, GRASP GL, JPEG XS
- TIFF extensions: Logluv HDR, Cloud Optimized GeoTIFF, OME-TIFF multi-channel
- Cross-platform support (replace `System.Drawing.Common` with a portable pixel source)
- NuGet packaging for all FileFormat libraries

## Known Limitations

- **Windows only** - Optimizer.Png and Optimizer.Gif use `System.Drawing.Common` which requires Windows. Compression.Core, Optimizer.Tiff, Optimizer.Bmp, Optimizer.Tga, Optimizer.Pcx, and Optimizer.Jpeg have no platform restrictions beyond `EnableWindowsTargeting` for `System.Drawing.Common` pixel input.
- **Lossy sub-byte modes** - Sub-byte Grayscale (1/2/4-bit) quantizes values, which can lose precision for mid-range grays.
- **16-bit precision** - Full 16-bit pipeline (Gray16, Rgb48, Rgba64) supported for scientific/HDR formats (FITS, EXR, DPX, Cineon, HDR, PFM, ENVI, PDS, Nifti, NRRD, BigTIFF, JPEG-LS, MRC, Grs16, Pco16Bit, Interfile). PixelConverter provides direct 16-to-16 routes and 8-to-16/16-to-8 conversion. Optimizer pipelines remain 8-bit only.
- **JPEG Chroma422** - BitMiracle.LibJpeg.NET does not implement 4:2:2 chroma subsampling; only 4:4:4 and 4:2:0 are supported.
- **VP8 lossy** - Encoder produces basic keyframe-only output; advanced features (multi-pass, partition threading) not implemented.
- **Codec subset implementations** - HEIF/AVIF/BPG codecs support I-frame only, single tile, YCbCr 4:2:0 8-bit. JPEG XL supports modular mode only (VarDCT lossy deferred). Camera RAW supports DNG lossless JPEG, Canon CR2 (lossless JPEG with slice reassembly), Nikon NEF (compressed with dual Huffman tables), and Sony ARW2 (7-bit delta); other manufacturer-specific compressions are future work.
- **PDF image extraction** - Extracts embedded raster images only; does not render vector graphics, text, or pages.
- **PE resource extraction** - Read-only; PE writing is not supported due to complexity of PE format.
