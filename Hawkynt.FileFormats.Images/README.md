# Hawkynt.FileFormats.Images

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)
![Formats](https://img.shields.io/badge/formats-850%2B-brightgreen)
![Reflection](https://img.shields.io/badge/runtime%20reflection-zero-success)

> One drop-in pure-C# package for detecting, reading, writing, and converting an unusually broad range of image formats through one source-generated registry and one platform-independent `RawImage` model.

## 📦 Installation

```bash
dotnet add package Hawkynt.FileFormats.Images
```

The package targets `net8.0`. Format implementations and the support libraries they need are bundled behind the public package API; consumers do not need one NuGet package per image format.

## ✨ Features

- 850+ registered formats spanning web, desktop, scientific, professional, fax, console, retro-computing, texture, icon/cursor, and document-preview formats.
- Source-generated `ImageFormat` enum and `FormatRegistry`; no runtime reflection is needed for registration or dispatch.
- Magic-byte, extension, MIME-type, file, byte-span, and stream detection.
- Common `RawImage` representation for cross-format conversion.
- Runtime read/write capability discovery instead of caller-maintained format lists.
- Multi-image access where a format exposes pages, frames, entries, or embedded images.
- Optional metadata-only `ImageInfo` paths when dimensions/properties can be read without decoding all pixels.
- Per-format `VideoMode` metadata for fixed dimensions, palette restrictions, pixel aspect ratios, and display filters used by historical formats.

## 🧩 Format support

The source-generated registry is authoritative for current capabilities:

```csharp
foreach (var entry in FormatRegistry.AllFormats.OrderBy(e => e.Name))
  Console.WriteLine($"{entry.Name}: read={entry.SupportsRead}, write={entry.SupportsWrite}");
```

[`../Formats.md`](../Formats.md) is the broad repository cross-reference. It is maintained by hand and can lag the registry, so use `FormatRegistry` when exact current read/write state matters.

### Common and modern formats

| Format | Extensions | Read | Write | Multi-image | MIME | Reference |
| --- | --- | :---: | :---: | :---: | --- | --- |
| [PNG](https://en.wikipedia.org/wiki/PNG) | `.png` | ✅ | ✅ | — | `image/png` | [W3C PNG](https://www.w3.org/TR/png-3/) |
| [JPEG](https://en.wikipedia.org/wiki/JPEG) | `.jpg`, `.jpeg`, `.jfif`, … | ✅ | ✅ | — | `image/jpeg` | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [GIF](https://en.wikipedia.org/wiki/GIF) | `.gif` | ✅ | ✅ | ✅ | `image/gif` | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| [BMP](https://en.wikipedia.org/wiki/BMP_file_format) | `.bmp`, `.dib` | ✅ | ✅ | — | `image/bmp` | [Microsoft bitmap storage](https://learn.microsoft.com/windows/win32/gdi/bitmap-storage) |
| [TIFF](https://en.wikipedia.org/wiki/TIFF) | `.tif`, `.tiff` | ✅ | ✅ | ✅ | `image/tiff` | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| [WebP](https://en.wikipedia.org/wiki/WebP) | `.webp` | ✅ | ✅ | ⚠️ | `image/webp` | [WebP RIFF container](https://developers.google.com/speed/webp/docs/riff_container) |
| [AVIF](https://en.wikipedia.org/wiki/AVIF) | `.avif` | ⚠️ | — | — | `image/avif` | [AOMedia AVIF](https://aomediacodec.github.io/av1-avif/) |
| [HEIF / HEIC](https://en.wikipedia.org/wiki/High_Efficiency_Image_File_Format) | `.heif`, `.heic` | ⚠️ | — | ⚠️ | `image/heic` | [Nokia HEIF](https://nokiatech.github.io/heif/) |
| [APNG](https://en.wikipedia.org/wiki/APNG) | `.apng`, `.png` | ✅ | ✅ | ✅ | `image/apng` | [APNG specification](https://wiki.mozilla.org/APNG_Specification) |
| [MNG](https://en.wikipedia.org/wiki/Multiple-image_Network_Graphics) | `.mng` | ✅ | ⚠️ | ✅ | `video/x-mng` | [MNG specification](http://www.libpng.org/pub/mng/spec/) |
| [QOI](https://en.wikipedia.org/wiki/QOI_(image_format)) | `.qoi` | ✅ | ✅ | — | `image/qoi` | [QOI specification](https://qoiformat.org/qoi-specification.pdf) |
| [JPEG XL](https://en.wikipedia.org/wiki/JPEG_XL) | `.jxl` | ⚠️ | ⚠️ | — | `image/jxl` | [JPEG XL](https://jpeg.org/jpegxl/) |
| [JPEG 2000](https://en.wikipedia.org/wiki/JPEG_2000) | `.jp2`, `.j2k`, … | ✅ | ⚠️ | — | `image/jp2` | [JPEG 2000](https://jpeg.org/jpeg2000/) |
| [JPEG XR](https://en.wikipedia.org/wiki/JPEG_XR) | `.jxr`, `.wdp`, `.hdp` | ✅ | ⚠️ | — | `image/jxr` | [ITU-T T.832](https://www.itu.int/rec/T-REC-T.832) |
| [BPG](https://en.wikipedia.org/wiki/Better_Portable_Graphics) | `.bpg` | ✅ | — | — | — | [Fabrice Bellard's BPG](https://bellard.org/bpg/) |
| [FLIF](https://en.wikipedia.org/wiki/Free_Lossless_Image_Format) | `.flif` | ✅ | — | — | — | [FLIF project](https://flif.info/) |

`⚠️` means a material subset or interoperability limitation exists. In particular, AVIF and HEIF have no registered encoder; JPEG XL container/header handling is useful, but its current pixel codec is not interoperable with arbitrary libjxl files and its writer must not be treated as a conforming general-purpose JPEG XL encoder.

### Scientific, HDR, and professional formats

| Format | Read | Write | Reference |
| --- | :---: | :---: | --- |
| [OpenEXR](https://en.wikipedia.org/wiki/OpenEXR) | ✅ | ✅ | [OpenEXR project](https://openexr.com/) |
| [Radiance HDR / RGBE](https://en.wikipedia.org/wiki/RGBE_image_format) | ✅ | ✅ | [Radiance](https://www.radiance-online.org/) |
| [FITS](https://en.wikipedia.org/wiki/FITS) | ✅ | ⚠️ | [NASA FITS](https://fits.gsfc.nasa.gov/) |
| [NRRD](https://en.wikipedia.org/wiki/Nrrd) | ✅ | ⚠️ | [Teem NRRD format](http://teem.sourceforge.net/nrrd/format.html) |
| [NIfTI](https://en.wikipedia.org/wiki/Neuroimaging_Informatics_Technology_Initiative) | ✅ | ⚠️ | [NIfTI](https://nifti.nimh.nih.gov/) |
| [DPX](https://en.wikipedia.org/wiki/Digital_Picture_Exchange) | ✅ | ✅ | [LOC DPX overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000178.shtml) |
| [Cineon](https://en.wikipedia.org/wiki/Cineon) | ✅ | ✅ | [LOC Cineon overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000180.shtml) |
| [PFM](https://en.wikipedia.org/wiki/Netpbm#PFM_graphic_format) | ✅ | ✅ | [Paul Debevec's PFM notes](https://www.pauldebevec.com/Research/HDR/PFM/) |

### Historical and platform formats

The package also contains hundreds of platform-specific and historical formats: Amiga/Atari/C64/CPC/Apple II, console graphics, fax formats, paint-program files, icon/cursor families, texture containers, and hardware screen dumps. Listing all of them twice would make the NuGet landing page worse; use the complete [`Formats.md`](../Formats.md) table or enumerate `FormatRegistry.AllFormats`.

## 🚀 Quick start

```csharp
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

var input = new FileInfo("mystery.bin");

var format = FormatRegistry.DetectFromFile(input);
var raw = FormatRegistry.Read(input);
var png = FormatRegistry.Write(raw!, ImageFormat.Png);

File.WriteAllBytes("out.png", png!);
```

### Detect from different inputs

```csharp
ImageFormat fromExtension = FormatRegistry.DetectFromExtension(".webp");
ImageFormat fromMime = FormatRegistry.DetectFromMimeType("image/png");
ImageFormat fromBytes = FormatRegistry.DetectFromBytes(headerBuffer);

using var stream = File.OpenRead("photo.bin");
ImageFormat fromStream = FormatRegistry.DetectFromStream(stream);

// For non-seekable streams, get a replayable stream back with the detected format.
var (detected, replay) = FormatRegistry.DetectFromStreamRewound(networkStream);
RawImage? image = FormatRegistry.Read(replay);
```

### Inspect capabilities before writing

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.WebP);
if (entry?.SupportsWrite == true) {
  var bytes = FormatRegistry.Write(raw, ImageFormat.WebP);
  File.WriteAllBytes("out.webp", bytes!);
}
```

### Multi-image formats

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Tiff);
if (entry?.SupportsMultiImage == true) {
  var pages = entry.GetImageCount!(new FileInfo("multi.tif"));
  for (var i = 0; i < pages; ++i) {
    RawImage? page = entry.LoadRawImageAtIndex!(new FileInfo("multi.tif"), i);
    // ...
  }
}
```

## 📚 API

### `FormatRegistry`

| Member | Purpose |
| --- | --- |
| `DetectFromExtension(string)` | Map an extension to `ImageFormat`. |
| `DetectFromMimeType(string)` | Map MIME type or alias to `ImageFormat`. |
| `DetectFromBytes(ReadOnlySpan<byte>)` | Detect from magic bytes/custom signature logic. |
| `DetectFromStream(Stream, int)` | Detect while restoring seekable-stream position. |
| `DetectFromStreamRewound(Stream, int)` | Detect and return a replayable stream for non-seekable inputs. |
| `DetectFromFile(FileInfo)` | Magic detection with extension fallback. |
| `Read(FileInfo / byte[] / Stream)` | Detect and decode to `RawImage`. |
| `Write(RawImage, ImageFormat)` | Encode to bytes when a writer exists. |
| `Write(RawImage, ImageFormat, Stream)` | Encode directly to a stream. |
| `GetEntry(ImageFormat)` | Get extensions, MIME types, signatures and capabilities. |
| `AllFormats` | Enumerate every registered format. |
| `SupportedReadFormats` / `SupportedWriteFormats` | Enumerate by capability. |

### `RawImage`

`RawImage` is the platform-independent decoded image exchanged by every format:

```csharp
public sealed class RawImage {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required PixelFormat Format { get; init; }
  public required byte[] PixelData { get; init; }

  public byte[]? Palette { get; init; }
  public int PaletteCount { get; init; }
  public byte[]? AlphaTable { get; init; }

  public byte[] ToBgra32();
  public byte[] ToRgba32();
  public byte[] ToRgb24();
}
```

Common pixel layouts include BGRA/RGBA/ARGB 32-bit, RGB/BGR 24-bit, 8/16-bit grayscale, indexed 1/4/8-bit, RGB565, RGB48, and RGBA64.

### `FormatEntry`

| Property | Meaning |
| --- | --- |
| `PrimaryExtension` / `AllExtensions` | Canonical extension and aliases. |
| `PrimaryMimeType` / `MimeTypes` | Preferred MIME type and aliases. |
| `SupportsRead` | A decoder is registered. |
| `SupportsWrite` | An arbitrary `RawImage` can be encoded. |
| `SupportsMultiImage` | Multiple frames/pages/entries can be addressed. |
| `ReadImageInfo` | Optional metadata-only path. |

## 🏗️ Architecture

`FileFormat.Registry.Generator` scans the compilation at build time and emits the registry and `ImageFormat` enum. Function pointers are wired directly to format implementations; the runtime does not discover formats through reflection.

Each format lives under `Formats/<Name>/` in its own namespace and implements the relevant static contracts from `FileFormat.Core`. Adding a format extends the generated registry automatically.

Detection is priority ordered: custom matchers handle formats whose identity is more complex than one fixed signature, then magic-byte signatures are checked. Extension fallback is used by `DetectFromFile` only after byte-level detection fails.

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| `FileFormat.Core` | `RawImage`, pixel formats, metadata and format contracts; bundled from this repository. |
| `Compression.Core` | Shared compression primitives used by several formats; bundled. |
| `FileFormat.TextMode` | Text-mode image infrastructure; bundled. |
| `BitMiracle.LibTiff.NET` | Managed TIFF support used by TIFF paths. |
| `BitMiracle.LibJpeg.NET` | Managed JPEG support used by JPEG paths. |
| `FrameworkExtensions.Backports` | Backported framework primitives. |
| `System.IO.Hashing` | Managed hashing primitives. |

## ⚠️ Limitations

- Coverage breadth is larger than conformance depth. Some historical formats have scarce or no public samples; registry presence is not a promise that every obscure producer variant has been verified.
- AVIF/HEIF and JPEG XL demonstrate why registration and conformance are separate questions: unsupported writers stay unregistered, and partial codecs are documented as partial rather than counted as complete interoperability.
- Writers are added only when another implementation or specification-based validator can check the result. A reader and writer agreeing only with each other is not treated as sufficient evidence.
- [`../Formats.md`](../Formats.md) is a useful cross-reference, but the runtime registry is authoritative for current read/write capability.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../LICENSE).
