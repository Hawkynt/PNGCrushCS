# PNGCrushCS

[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
[![Language](https://img.shields.io/github/languages/top/Hawkynt/PNGCrushCS?color=8957D5)](https://github.com/Hawkynt/PNGCrushCS)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/PNGCrushCS?branch=main)
![Activity](https://img.shields.io/github/commit-activity/m/Hawkynt/PNGCrushCS)
[![Release](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/releases/latest)
[![Nightly](https://img.shields.io/github/v/release/Hawkynt/PNGCrushCS?include_prereleases&sort=date&filter=nightly-*&label=nightly&color=FF9800)](https://github.com/Hawkynt/PNGCrushCS/releases)
[![NuGet Images](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Images?label=Images)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)

> A pure-managed C# image and video format suite plus an exhaustive image optimizer: broad format detection/read/write APIs, container/codec support, and lossless size optimization behind one repository.

## 🧩 Components

| Component | Kind | Description |
| --- | --- | --- |
| **[`Hawkynt.FileFormats.Images`](Hawkynt.FileFormats.Images/README.md)** | NuGet | Public image-format package with a source-generated, zero-reflection registry. Start here for general image detection/read/write. |
| **[`Hawkynt.FileFormats.Video`](Hawkynt.FileFormats.Video/README.md)** | NuGet | Video containers and codecs with separate demux/decode/encode/mux contracts; decoded frames are `RawImage`s. |
| **[`Hawkynt.ImageTransformUI`](Hawkynt.ImageTransformUI/README.md)** | NuGet | Shared WinForms color-reduction UI backed by FrameworkExtensions quantizer/ditherer registries. |
| **`Crush.Image`** | CLI | Auto-detects input and runs format-specific optimization and optional cross-format conversion. |
| **`Crush.Viewer`** | WinForms | Opens registered image formats with zoom, pan, multi-image navigation, transforms, and Save As conversion. |
| **`Compression.Core`** | Library | Pure-C# DEFLATE/LZW/PackBits primitives, including Zopfli-class parsing used by optimizers. |
| **`FileFormat.Core`** | Library | Shared `RawImage`, format contracts, metadata, detection primitives, and pixel conversion infrastructure. |
| **`Optimizer.*`** | Libraries | Per-format lossless optimization engines. What each one does is tabled in the [image package README](Hawkynt.FileFormats.Images/README.md#-format-support). |

The complete image support table (read, write, metadata-only, multi-image, optimizer, per format) lives in the [image package README](Hawkynt.FileFormats.Images/README.md#-format-support). Video container/codec details live in [`Hawkynt.FileFormats.Video/codec-coverage.md`](Hawkynt.FileFormats.Video/codec-coverage.md).

## 🖼️ Viewer

`Crush.Viewer` is the Windows desktop front end for the same format registry used by the libraries. It supports drag-and-drop/open, zoom and pan, multi-image navigation, crop/resize/rotate/flip, palette reduction, text-mode rendering, and conversion through **Save As**.

![Crush Viewer showing the deterministic screenshot fixture](docs/screenshots/crush-viewer.png)

The screenshot is not maintained by hand. Every push to a branch other than `main` builds the viewer on Windows, opens a deterministic PNG fixture through the real decoder, captures the rendered WinForms client area, and commits a changed screenshot back to that branch. Screenshot-only commits do not recursively trigger another capture.

## 🚀 CLI usage

```bash
# Auto-detect, try same-format optimization plus cross-format conversion, keep the smallest result
crush auto -i input.png -o output.png

# Format-specific verbs
crush png  -i input.png  -o output.png
crush jpeg -i input.jpg  -o output.jpg
crush gif  -i input.gif  -o output.gif
crush tiff -i input.tif  -o output.tif
crush bmp | tga | pcx | ico | cur | ani | webp -i ... -o ...
```

Common options: `--input/-i`, `--output/-o`, `--jobs/-j` (`0` = all cores), and `--verbose/-v`. Run `crush <verb> --help` for format-specific controls.

### Optimization strategy

Every optimizer follows the same basic pipeline:

1. Decode input into platform-independent pixel data or a losslessly editable container representation.
2. Generate valid encoding combinations for that format.
3. Test combinations in parallel with bounded concurrency and pooled buffers where useful.
4. Optionally screen candidates with a cheaper compressor before expensive Ultra/Hyper passes.
5. Return the smallest valid result.

## 🧩 Optimizer format support

| Format | Optimize | Typical search dimensions | Reference |
| --- | :---: | --- | --- |
| [PNG](https://en.wikipedia.org/wiki/PNG) | ✅ | color type × bit depth × filters × DEFLATE × interlace × palette/quantization | [W3C PNG](https://www.w3.org/TR/png-3/) |
| [JPEG](https://en.wikipedia.org/wiki/JPEG) | ✅ | lossless coefficient transcode; optional lossy mode × quality × subsampling × Huffman × metadata | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [GIF](https://en.wikipedia.org/wiki/GIF) | ✅ | palette strategy × global/local tables × disposal × trimming × frame differencing × LZW policy | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| [TIFF](https://en.wikipedia.org/wiki/TIFF) | ✅ | color mode × compression × predictor × strip/tile layout | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| [BMP](https://en.wikipedia.org/wiki/BMP_file_format) | ✅ | color mode × RLE4/RLE8 × row order × palette ordering | [Microsoft bitmap storage](https://learn.microsoft.com/windows/win32/gdi/bitmap-storage) |
| [TGA](https://en.wikipedia.org/wiki/Truevision_TGA) | ✅ | color mode × pixel width × RLE × origin | [TGA 2.0 overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000180.shtml) |
| [PCX](https://en.wikipedia.org/wiki/PCX) | ✅ | color mode × plane layout × palette ordering | [PCX format notes](http://fileformats.archiveteam.org/wiki/PCX) |
| [ICO](https://en.wikipedia.org/wiki/ICO_(file_format)) / [CUR](https://en.wikipedia.org/wiki/ICO_(file_format)) | ✅ | per-entry DIB vs PNG choice; CUR hotspot preservation | [Microsoft icons](https://learn.microsoft.com/windows/win32/menurc/about-icons) |
| [ANI](https://en.wikipedia.org/wiki/ANI_(file_format)) | ✅ | embedded cursor choices while preserving RIFF ACON structure | [Microsoft RIFF](https://learn.microsoft.com/windows/win32/xaudio2/resource-interchange-file-format--riff-) |
| [WebP](https://en.wikipedia.org/wiki/WebP) | ✅ | container-level pass-through and metadata stripping; codecs provided by the format library | [WebP RIFF container](https://developers.google.com/speed/webp/docs/riff_container) |

The PNG/GIF/TIFF engines are also published as packages; their READMEs contain package-level capability matrices and API examples.

## 🏗️ Architecture

```text
FileFormat.Core       contracts, RawImage, metadata, detection primitives
Compression.Core      DEFLATE / LZW / PackBits
FileFormat.TextMode   text-screen model and bitmap fonts

Hawkynt.FileFormats.Images
  Formats/<Name>/                 one namespace/folder per image format
  source-generated FormatRegistry / ImageFormat

Hawkynt.FileFormats.Video
  Formats/<Name>/                 container demux/mux
  Codecs/                         codec decode/encode
  source-generated VideoFormatRegistry / VideoFormat

Crush.Core
Optimizer.*                        exhaustive per-format optimizers
Crush.Image                        unified CLI
Crush.Viewer                       WinForms viewer / converter
```

Key design rules:

- Image/video format libraries are managed C# and avoid native dependencies.
- Demuxing and decoding are separate; a remux does not imply decode/re-encode.
- `RawImage` is the common decoded-frame/image representation.
- Registry population happens at compile time rather than through runtime reflection.
- Writers are added only when external/reference tooling can validate the generated files.

## 🛠️ Build / test / run

PNGCrushCS can optionally consume the sibling `CompressionWorkbench` checkout for linked-source primitives:

```text
work/
├─ PNGCrushCS/
└─ CompressionWorkbench/
```

```bash
git clone https://github.com/Hawkynt/PNGCrushCS.git
git clone https://github.com/Hawkynt/CompressionWorkbench.git
cd PNGCrushCS

# Build
dotnet build PngCrush.slnx -c Release

# Required test tier (same category policy as CI)
for proj in Tests/*/*.csproj; do
  dotnet test "$proj" --filter "TestCategory!=Regression&TestCategory!=Performance"
done

# Run the unified CLI
dotnet run --project Crush.Image -- auto -i input.png -o output.png

# Run the viewer on Windows
dotnet run --project Crush.Viewer -- input.png
```

The cross-repo links are conditional; consumers of the published packages do not need the sibling checkout.

## 🤖 CI

`ci.yml` validates pull requests and pushes. `viewer-screenshot.yml` refreshes the README screenshot on pushes to non-`main` branches. `release.yml` handles coordinated releases and NuGet publishing. Version stamping is performed by `.github/workflows/scripts/version.pl --stamp` during CI.

Stable releases are manual. Nightlies are generated from green `main` builds.

## 💡 Inspiration

The breadth of format coverage is informed by tools that have spent decades dealing with the less fashionable corners of image-format history:

| Project | Focus | Link |
| --- | --- | --- |
| Tom's Editor | Very broad conversion coverage | [Supported formats](https://tomseditor.com/convert/supported-formats) |
| ImageMagick | General-purpose image processing | [Format list](https://imagemagick.org/script/formats.php) |
| XnView | Broad viewer/converter support | [Formats](https://www.xnview.com/en/xnview/#formats) |
| IrfanView | Viewer with plugin ecosystem | [Formats](https://www.irfanview.com/main_formats.htm) |

These are comparison/inspiration sources, not implementation specifications. Format implementations should cite the normative specification, original paper, or authoritative project where possible.

## ✅ Conformance and scope

Capability claims are kept next to the implementation evidence instead of duplicated as a hand-maintained backlog in this README:

- [`Hawkynt.FileFormats.Images/README.md`](Hawkynt.FileFormats.Images/README.md) is the image support table — read, write, metadata-only, multi-image and optimizer per format — together with the interoperability evidence for the modern codecs (WebP, AVIF, HEIF, JPEG XL, JPEG 2000, JPEG XR) and the oracle-corpus results.
- [`Hawkynt.FileFormats.Video/codec-coverage.md`](Hawkynt.FileFormats.Video/codec-coverage.md) records video codec/container coverage.
- [`Tools/parity/README.md`](Tools/parity/README.md) is how those numbers are measured against RECOIL, ImageMagick, ffmpeg, XnView and IrfanView.

A capability is promoted only when its syntax and behavior have evidence beyond the project's own writer reading its own output. Unsupported profiles stay unadvertised rather than being represented as successful decode/write support.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
