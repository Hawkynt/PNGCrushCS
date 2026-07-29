# PNGCrushCS

[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
[![Language](https://img.shields.io/github/languages/top/Hawkynt/PNGCrushCS?color=8957D5)](https://github.com/Hawkynt/PNGCrushCS)

[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/PNGCrushCS?branch=main)
![Activity](https://img.shields.io/github/commit-activity/m/Hawkynt/PNGCrushCS)

[![Stars](https://img.shields.io/github/stars/Hawkynt/PNGCrushCS?color=FFD700)](https://github.com/Hawkynt/PNGCrushCS/stargazers)
[![Forks](https://img.shields.io/github/forks/Hawkynt/PNGCrushCS?color=008080)](https://github.com/Hawkynt/PNGCrushCS/network/members)
[![Issues](https://img.shields.io/github/issues/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/issues)
![Code Size](https://img.shields.io/github/languages/code-size/Hawkynt/PNGCrushCS?color=4CAF50)
![Repo Size](https://img.shields.io/github/repo-size/Hawkynt/PNGCrushCS?color=FF9800)

[![Release](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/releases/latest)
[![Nightly](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS?include_prereleases&sort=date&filter=nightly-*&label=nightly&color=FF9800)](https://github.com/Hawkynt/PNGCrushCS/releases)
[![Downloads](https://img.shields.io/github/downloads/Hawkynt/PNGCrushCS/total)](https://github.com/Hawkynt/PNGCrushCS/releases)
[![NuGet Images](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Images?label=Images)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)

> A C# image suite that ships **(a)** a NuGet meta-package for reading, writing, and detecting a broad range of image formats in pure managed code, and **(b)** a CLI optimizer that exhaustively tries every valid encoding combination to produce the smallest lossless output for PNG, JPEG, GIF, TIFF, BMP, TGA, PCX, ICO, CUR, ANI, and WebP — backed by a custom Zopfli-class DEFLATE encoder.

## What's in this repository

| Component | Description |
|---|---|
| **[`Hawkynt.FileFormats.Images`](Hawkynt.FileFormats.Images/README.md)** (NuGet) | Public meta-package: format reader/writers behind one zero-reflection static registry. **Start here if you just want to read/write images.** |
| **`Crush.Image`** (CLI) | Single unified CLI that auto-detects input format and runs an exhaustive optimizer across its supported raster formats with optional cross-format conversion. |
| **`Compression.Core`** | Pure RFC 1951 DEFLATE with Zopfli-class optimal parsing (Ultra: 2-pass DP, dual hash chain depths; Hyper: parallel hash chains, iterative refinement, block splitting). No platform dependencies. |
| **`FileFormat.*`** (one library per format) | Standalone reader/writer per format. Used by both the NuGet package and the CLI optimizers. |
| **`Optimizer.*`** (one engine per optimizable format) | Per-format optimization engines. Each generates every valid combination of encoding parameters, compresses each in parallel, and keeps the smallest result. |

For the comprehensive **list of supported file formats** (read/write capabilities, MIME types, reference links), see the [`Hawkynt.FileFormats.Images` README](Hawkynt.FileFormats.Images/README.md#supported-formats).

## 🚀 CLI usage

```bash
# Auto-detect, try same-format optimization plus cross-format conversion, keep smallest
crush auto -i input.png -o output.png

# Format-specific verb with format-specific options
crush png  -i input.png  -o output.png
crush jpeg -i input.jpg  -o output.jpg
crush gif  -i input.gif  -o output.gif
crush tiff -i input.tif  -o output.tif
crush bmp | tga | pcx | ico | cur | ani | webp -i ... -o ...
```

Common options across all verbs: `--input/-i`, `--output/-o`, `--jobs/-j` (parallelism, `0` = all cores), `--verbose/-v`. Each verb adds format-specific knobs (PNG filter strategies, GIF palette reordering, TIFF compression methods, JPEG quality levels, etc.). See `crush <verb> --help` for the full per-verb option list.

### Optimization strategy (all formats)

Each per-format optimizer follows the same pipeline:

1. Decode input to platform-independent pixel data.
2. Generate every valid combination of encoding parameters (color mode × bit depth × filter/predictor × compression × row order × …).
3. Test each combination in parallel via `SemaphoreSlim`, with `ArrayPool`-backed buffers and concurrent best-result tracking.
4. Optional two-phase: screen all combos with fast compression, then re-test the top N candidates with expensive Ultra/Hyper methods.
5. Return the smallest valid result.

| Optimizer | Highlights |
|---|---|
| **PNG**  | Color mode × bit depth × filter strategy × deflate × interlace × quantizer/ditherer; SIMD filters; deflate-aware filter selection; tRNS generation; palette reordering (Hilbert/SpatialLocality/DeflateOptimized); FrameworkExtensions Wu/Octree/MedianCut/Neuquant/PngQuant × Floyd-Steinberg/Atkinson/Sierra/Bayer4×4 |
| **GIF**  | Palette strategy × color table × disposal × margin trimming × frame differencing × deferred clear codes; LZW with deferred-clear adaptive interval; palette-aware frame deduplication |
| **TIFF** | Color mode × compression (None/PackBits/LZW/DEFLATE/Zopfli) × predictor × strip/tile size; horizontal differencing predictor; tiled encoding; multi-page IFD chains |
| **BMP**  | 7 color modes × RLE8/RLE4 × row order; palette frequency sorting |
| **TGA**  | 5 color modes × pixel-width-aware RLE × origin; TGA 2.0 footer |
| **PCX**  | 5 color modes × plane configuration × palette ordering |
| **JPEG** | Lossless DCT coefficient transcode (no generation loss) + opt-in lossy re-encode (mode × quality × subsampling × Huffman × metadata) |
| **ICO/CUR/ANI** | Per-entry BMP-DIB vs PNG selection; 2ⁿ combination search (capped at 256); CUR preserves hotspots; ANI preserves RIFF ACON structure |
| **WebP** | Container-level: VP8/VP8L pass-through with optional metadata stripping (EXIF/ICCP/XMP). Full pure-C# VP8 lossy + VP8L lossless codecs available via `FileFormat.WebP`. |

## Architecture

```
FileFormat.Core       contracts (RawImage, the static-abstract format interfaces) and the
                      per-machine primitives formats share (Atari8BitGraphics,
                      Commodore64Graphics, ZxSpectrumGraphics, PlanarConverter, …)
Compression.Core      deflate / LZW / PackBits, used by the PNG, TIFF and JPEG XL codecs
FileFormat.TextMode   text-screen model and bitmap fonts; multi-targeted because the
                      WinForms UI consumes it on net48 as well

Hawkynt.FileFormats.Images  <-- (public NuGet — every format lives here)
  Formats/<Name>/           one folder and one namespace per format, one assembly for all
  Source-generated FormatRegistry / ImageFormat enum over everything in the compilation

Crush.Core         <-- Crush.Image + the Optimizer.* libraries
Optimizer.Image    <-- BitmapConverter, ImageFormatDetector (Windows-specific glue)
```

- **Compression.Core** — pure BCL, no platform dependencies, no native code.
- **Formats** — every format is a folder under `Hawkynt.FileFormats.Images/Formats/` with its
  own namespace (`FileFormat.<Name>`), targeting `net8.0` with no platform dependencies. They
  were once one assembly apiece; sharing a single assembly lets them share helpers without
  making those helpers public.
- **Optimizer.Png / Optimizer.Gif** — Windows-only (use `System.Drawing.Common`).
- **Other Optimizer.\*** — `net8.0` with `EnableWindowsTargeting=true` for `System.Drawing.Common` pixel input.
- **Crush.\* CLI apps** — `net9.0` (Windows-only for `Crush.Png` on `net10.0-windows`).

For per-project details, build commands, and the comprehensive list of public types, see [`CLAUDE.md`](CLAUDE.md).

## 🛠️ Build / test / run

PNGCrushCS optionally consumes one sibling repo. Clone it next to this one before building if you want the linked-source primitives:

```bash
# Working directory layout expected by csproj relative paths
work/
├─ PNGCrushCS/             # this repo
└─ CompressionWorkbench/   # FileFormat.Jpeg + others link source files from Compression.Core

# Clone both
git clone https://github.com/Hawkynt/PNGCrushCS.git
git clone https://github.com/Hawkynt/CompressionWorkbench.git
```

The cross-repo source links are conditional — when the sibling is absent the project falls back to its non-sibling code path, so consumers of the published NuGet package don't need it. The clone is only required when **building from source** AND you want the CompressionWorkbench-enhanced paths.

```bash
cd PNGCrushCS

# Build
dotnet build PngCrush.slnx -c Release

# Run all tests in Tests/
for proj in Tests/*/*.csproj; do dotnet test "$proj"; done

# Run the unified CLI
dotnet run --project Crush.Image -- auto -i input.png -o output.png
```

## CI

GitHub Actions: `ci.yml` (PR/push), `release.yml` (tag pushes — produces CLI/viewer ZIPs and pushes the `Hawkynt.FileFormats.Images` NuGet package). Version stamping via `.github/workflows/scripts/version.pl --stamp` rewrites `<Version>X.Y.Z</Version>` to `X.Y.Z.<git-rev-list-count>` at build time.

## Inspiration

The format coverage of this project is inspired by the breadth of these tools:

- [Tom's Editor](https://tomseditor.com/convert/supported-formats) — 600+ formats
- [ImageMagick](https://imagemagick.org/script/formats.php) — 200+ formats
- [XnView](https://www.xnview.com/en/xnview/#formats) — 500+ formats
- [IrfanView](https://www.irfanview.com/main_formats.htm) — 100+ formats via plugins

## Known limitations

- **Windows-only optimizers** — `Optimizer.Png` and `Optimizer.Gif` use `System.Drawing.Common`. The `FileFormat.*` libraries and `Hawkynt.FileFormats.Images` package are cross-platform.
- **16-bit precision** — full 16-bit pipeline is supported for read/write of scientific/HDR formats (FITS, EXR, DPX, Cineon, HDR, PFM, ENVI, PDS, Nifti, NRRD, BigTIFF, JPEG-LS, MRC, etc.). Optimizer pipelines remain 8-bit only.
- **VP8 lossy encoder** — keyframe-only output; multi-pass rate control and partition threading are deferred. Alpha is preserved bit-exactly via the ALPH chunk (uncompressed method 0).
- **Codec subsets** — HEIF/AVIF/BPG decoders are I-frame only, single tile, YCbCr 4:2:0 8-bit. JPEG XL: container (FF 0A signature, ftyp/jxlc/jxlp boxes) + SizeHeader + ImageMetadata + FrameHeader (ISO/IEC 18181-1 §3.6.2 / §3.6.3 / §3.6.5) are spec-conformant — real JPEG XL files are detected, dimensions extracted, image metadata (bit depth, color encoding, extra channels) and frame metadata (frame type, encoding mode, passes) parsed. Pixel codec (modular sub-codec body and VarDCT) is the remaining workstream — arbitrary real-world `.jxl` files won't decode their pixel data yet. Camera RAW supports DNG lossless JPEG, Canon CR2, Nikon NEF, and Sony ARW2 — other manufacturer-specific compressions are future work.
- **Read-only formats** — of 547 registered formats all 547 decode, but only 344 can encode an arbitrary image; the other 203 parse and re-serialize a file they read without being able to author one from pixel data. PDF/PE-resource extraction is one-way by nature. See [`Formats.md`](Formats.md) for the per-format breakdown.
- **Third-party conformance** — of the 52 formats ImageMagick both reads and identifies, it decodes 47 of our encoder outputs at matching dimensions. EPS, Fax G3, Palm, RGF and RLA still produce output it rejects.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
