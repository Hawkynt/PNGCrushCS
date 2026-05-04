# Hawkynt.FileFormats.Images

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
![Target](https://img.shields.io/badge/target-net8.0-blue)
![Formats](https://img.shields.io/badge/formats-540%2B-brightgreen)
![Reflection](https://img.shields.io/badge/runtime%20reflection-zero-success)

> **One drop-in NuGet package for reading, writing, and detecting 540+ image formats — pure C#, zero runtime reflection, single static API.**

## Why?

`System.Drawing.Common` ships with PNG/JPEG/GIF/BMP/TIFF; `ImageSharp` adds WebP and a handful more. Everything else — every retro computing format, every fax encoding, every GPU texture container, every obscure scientific or professional format — is left to the consumer to glue together format-by-format.

This package is the union of every `FileFormat.*` library in [PNGCrushCS](https://github.com/Hawkynt/PNGCrushCS), exposed behind a single static registry generated at compile time. Each format is a fully native C# implementation — no native bindings, no platform restrictions (except TIFF via LibTiff.NET and JPEG via LibJpeg.NET, both managed wrappers).

Use it when you need to **detect what an arbitrary stream contains** and **decode it to a `RawImage`** without caring about the codec details.

## Install

```bash
dotnet add package Hawkynt.FileFormats.Images
```

The package bundles all 540 format DLLs in `lib/net8.0/`. Total size ≈ 3.7 MB. Zero NuGet dependencies pulled into your project.

## Quick start

```csharp
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

// Detect → decode → re-encode in 4 lines
var format = FormatRegistry.DetectFromFile(new FileInfo("mystery.bin"));
var raw    = FormatRegistry.Read(new FileInfo("mystery.bin"));
var bytes  = FormatRegistry.Write(raw!, ImageFormat.Png);
File.WriteAllBytes("out.png", bytes!);
```

## Common scenarios

### Detect a format

```csharp
// From a file (magic-byte detection, falls back to extension)
ImageFormat fmt = FormatRegistry.DetectFromFile(new FileInfo("photo.tga"));

// From raw bytes (e.g. the first 64 bytes of an HTTP response)
ImageFormat fmt = FormatRegistry.DetectFromBytes(headerBuffer);

// From a seekable stream — position is restored on return
using var fs = File.OpenRead("photo.bin");
ImageFormat fmt = FormatRegistry.DetectFromStream(fs);
// fs.Position == 0 here

// From a non-seekable stream (network, pipe) — get a buffered wrapper back
var (fmt, replay) = FormatRegistry.DetectFromStreamRewound(networkStream);
RawImage? img = FormatRegistry.Read(replay);  // replay re-emits the consumed prefix

// From a file extension (with or without leading dot)
ImageFormat fmt = FormatRegistry.DetectFromExtension(".webp");

// From a MIME type (case-insensitive, accepts aliases)
ImageFormat fmt = FormatRegistry.DetectFromMimeType("IMAGE/X-PNG");
```

### Read any format to `RawImage`

```csharp
RawImage? img = FormatRegistry.Read(new FileInfo("anything.tga"));
if (img != null) {
  Console.WriteLine($"{img.Width}x{img.Height} {img.Format} HasAlpha={img.HasAlpha}");
  byte[] rgba = img.ToRgba32();   // normalize for consumption
}
```

`Read` accepts `FileInfo`, `byte[]`, or `Stream`. All three auto-detect the format first, then dispatch to the matching decoder.

### Encode a `RawImage`

```csharp
var raw = new RawImage {
  Width = 256, Height = 256,
  Format = PixelFormat.Rgba32,
  PixelData = pixelBytes,                 // 256 * 256 * 4 = 262144 bytes
};

byte[]? png  = FormatRegistry.Write(raw, ImageFormat.Png);
byte[]? webp = FormatRegistry.Write(raw, ImageFormat.WebP);
byte[]? qoi  = FormatRegistry.Write(raw, ImageFormat.Qoi);

// Write straight into a stream (returns false if the format is read-only)
using var output = File.Create("out.bmp");
bool ok = FormatRegistry.Write(raw, ImageFormat.Bmp, output);
```

`Write` returns `null` for read-only formats. Check `entry.SupportsWrite` first if you need to validate at runtime.

### Look up extensions and MIME types

```csharp
string ext  = FormatRegistry.PrimaryExtension(ImageFormat.Jpeg);    // ".jpg"
var aliases = FormatRegistry.AllExtensions(ImageFormat.Jpeg);       // [".jpg", ".jpeg", ".jfif", ".jpe", ".thm"]

string mime = FormatRegistry.PrimaryMimeType(ImageFormat.WebP);     // "image/webp"
var mimes   = FormatRegistry.AllMimeTypes(ImageFormat.Png);         // ["image/png", "image/x-png"]
```

### Multi-image formats (animated GIF, multi-page TIFF, ICO sets, APNG, MNG)

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Tiff);

if (entry?.SupportsMultiImage == true) {
  int pages = entry.GetImageCount!(new FileInfo("multi.tif"));
  for (int i = 0; i < pages; i++) {
    RawImage? page = entry.LoadRawImageAtIndex!(new FileInfo("multi.tif"), i);
    // ...
  }

  // Or load them all at once
  var allPages = entry.LoadAllRawImages!(new FileInfo("multi.tif"));
}
```

### Enumerate formats with capability filtering

```csharp
// All formats that can both read and write
var roundTrippable = FormatRegistry.AllFormats
  .Where(e => e.SupportsRead && e.SupportsWrite)
  .OrderBy(e => e.Name);

// All formats containing multiple sub-images
var multiImage = FormatRegistry.AllFormats
  .Where(e => e.SupportsMultiImage);

// All formats with a registered MIME type (for content-type negotiation)
var mimed = FormatRegistry.AllFormats
  .Where(e => e.MimeTypes.Length > 0);

// Build a UI picker for "Save as..."
foreach (var entry in FormatRegistry.SupportedWriteFormats.OrderBy(e => e.Name))
  Console.WriteLine($"{entry.Name,-25} {entry.PrimaryExtension,-10} {entry.PrimaryMimeType}");
```

### Cross-format conversion

```csharp
// One-liner: read whatever, write as PNG
File.WriteAllBytes("out.png",
  FormatRegistry.Write(FormatRegistry.Read(new FileInfo("in.tga"))!, ImageFormat.Png)!);

// Convert a directory of mixed formats to QOI
foreach (var src in Directory.EnumerateFiles("photos")) {
  var raw = FormatRegistry.Read(new FileInfo(src));
  if (raw == null) continue;
  var dst = Path.ChangeExtension(src, ".qoi");
  File.WriteAllBytes(dst, FormatRegistry.Write(raw, ImageFormat.Qoi)!);
}
```

### Read metadata without decoding pixels

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Jpeg);
ImageInfo? info = entry?.ReadImageInfo?.Invoke(File.ReadAllBytes("photo.jpg"));
if (info is { } meta)
  Console.WriteLine($"{meta.Width}x{meta.Height} @ {meta.BitsPerPixel}bpp ({meta.ColorMode})");
```

`ReadImageInfo` is `null` for formats that don't have a fast metadata path; fall back to `Read` and inspect the resulting `RawImage`.

## API reference

### `FormatRegistry` (static class)

The single entry point. All members are static.

#### Detection

| Member | Description |
|---|---|
| `ImageFormat DetectFromExtension(string extension)` | Map a file extension (with or without leading dot) to a format. Returns `Unknown` if not registered. |
| `ImageFormat DetectFromMimeType(string mimeType)` | Map a MIME type string (case-insensitive) to a format. Aliases are accepted. |
| `ImageFormat DetectFromBytes(ReadOnlySpan<byte> header)` | Walk the priority-sorted magic-byte table against header bytes. 64 bytes is enough for every known format. |
| `ImageFormat DetectFromStream(Stream stream, int peekBytes = 64)` | Peek `peekBytes` from the stream and detect. For seekable streams the position is restored. |
| `(ImageFormat, Stream) DetectFromStreamRewound(Stream stream, int peekBytes = 64)` | Detect AND return a stream positioned at the original start. For non-seekable streams a buffered wrapper is returned. |
| `ImageFormat DetectFromFile(FileInfo file)` | Magic-byte detection first; falls back to extension if magic detection returns `Unknown`. |

#### Lookup

| Member | Description |
|---|---|
| `FormatEntry? GetEntry(ImageFormat format)` | Full entry for a format (null if unknown). |
| `string PrimaryExtension(ImageFormat format)` | Canonical extension like `".png"`. Empty string if unknown. |
| `IReadOnlyList<string> AllExtensions(ImageFormat format)` | Every recognized alias (e.g. `.tif`/`.tiff`). |
| `string PrimaryMimeType(ImageFormat format)` | Preferred IANA MIME type. `"application/octet-stream"` if not annotated. |
| `IReadOnlyList<string> AllMimeTypes(ImageFormat format)` | All registered MIME types in declaration order. |

#### Read / write

| Member | Description |
|---|---|
| `RawImage? Read(FileInfo file)` | Detect + decode a file. Returns null if format unrecognized or decode fails. |
| `RawImage? Read(byte[] data)` | Detect + decode a byte buffer. |
| `RawImage? Read(Stream stream)` | Reads the stream into memory, then detects + decodes. |
| `byte[]? Write(RawImage image, ImageFormat format)` | Encode. Returns null if the target format is read-only. |
| `bool Write(RawImage image, ImageFormat format, Stream output)` | Encode straight into a stream. Returns false if read-only. |

#### Enumeration

| Member | Description |
|---|---|
| `IEnumerable<FormatEntry> AllFormats` | Every registered format. |
| `IEnumerable<FormatEntry> SupportedReadFormats` | Formats with a working decoder. |
| `IEnumerable<FormatEntry> SupportedWriteFormats` | Formats with a working encoder. |

### `FormatEntry` (sealed record)

Returned by `GetEntry` and produced by the source-generated `RegisterAll()`. All function-pointer fields are typed (no reflection); they trim cleanly under `PublishTrimmed`/AOT.

```csharp
public sealed record FormatEntry(
  ImageFormat Format,
  string Name,
  string PrimaryExtension,
  string[] AllExtensions,
  string[] MimeTypes,
  FormatCapability Capabilities,
  MagicSignature[] MagicSignatures,
  Func<byte[], bool?>? MatchesSignature,
  int DetectionPriority,
  Func<FileInfo, RawImage?> LoadRawImage,
  Func<byte[], RawImage?> LoadRawImageFromBytes,
  Func<RawImage, byte[]>? ConvertFromRawImage,
  Func<byte[], ImageInfo?>? ReadImageInfo = null,
  Func<FileInfo, int>? GetImageCount = null,
  Func<FileInfo, int, RawImage?>? LoadRawImageAtIndex = null,
  Func<FileInfo, IReadOnlyList<RawImage>?>? LoadAllRawImages = null
);
```

| Computed property | Description |
|---|---|
| `string PrimaryMimeType` | First MIME type, or `"application/octet-stream"`. |
| `bool SupportsRead` | True if a reader is registered (always true for entries that exist). |
| `bool SupportsWrite` | True if `ConvertFromRawImage != null`. |
| `bool SupportsMultiImage` | True if `GetImageCount != null` (animated GIF, multi-page TIFF, ICO sets, APNG, MNG, FLI, DCX, MPO, ICNS). |

### `MagicSignature` (readonly record struct)

```csharp
public readonly record struct MagicSignature(
  byte[] Signature,
  int Offset,
  int MinHeaderLength);
```

Emitted at compile time from `[FormatMagicBytes(...)]` attributes. `MinHeaderLength` is always `Offset + Signature.Length` and lets the detector skip signatures whose required header range is longer than the bytes it has.

### `ImageFormat` (auto-generated enum)

Compile-time enumeration of every registered format. The first member is `Unknown = 0`. Entries are stable across builds within the same set of referenced format libraries; adding a new `FileFormat.<Name>` library extends the enum but does not renumber existing entries.

```csharp
public enum ImageFormat {
  Unknown = 0,
  Png, Jpeg, Gif, Bmp, Tiff, WebP, Avif, Apng, Mng, Qoi,
  Tga, Pcx, Ico, Cur, Ani, Sgi, Wbmp, Aai, Hrz, Cmu,
  // ... ~530 more
}
```

### `RawImage` (from `FileFormat.Core`)

The platform-independent pixel buffer used for all reads/writes.

```csharp
public sealed class RawImage {
  public required int         Width    { get; init; }
  public required int         Height   { get; init; }
  public required PixelFormat Format   { get; init; }
  public required byte[]      PixelData { get; init; }   // layout determined by Format

  public byte[]? Palette      { get; init; }   // RGB triplets, 3 bytes/entry, for indexed formats
  public int     PaletteCount { get; init; }
  public byte[]? AlphaTable   { get; init; }   // optional per-palette alpha (PNG tRNS-style)

  public bool IsIndexed { get; }                     // computed
  public bool HasAlpha  { get; }                     // computed (alpha-table aware)

  public byte[] ToBgra32();                          // normalize to 32bpp BGRA
  public byte[] ToRgba32();                          // normalize to 32bpp RGBA
  public byte[] ToRgb24();                           // normalize to 24bpp RGB

  public static int BytesPerPixel(PixelFormat format);
  public static int BitsPerPixel (PixelFormat format);
}
```

### `PixelFormat` (enum)

| Value | Layout | Bits |
|---|---|---|
| `Bgra32` | B, G, R, A | 32 |
| `Rgba32` | R, G, B, A | 32 |
| `Argb32` | A, R, G, B | 32 |
| `Rgb24`  | R, G, B    | 24 |
| `Bgr24`  | B, G, R    | 24 |
| `Gray8`  | G          |  8 |
| `Gray16` | G (big-endian)        | 16 |
| `GrayAlpha16` | G, A             | 16 |
| `Indexed8`    | palette index    |  8 |
| `Indexed4`    | sub-byte         |  4 |
| `Indexed1`    | sub-byte         |  1 |
| `Rgba64`      | R, G, B, A (16bit each) | 64 |
| `Rgb48`       | R, G, B (16bit each)    | 48 |
| `Rgb565`      | RRRRR-GGGGGG-BBBBB      | 16 |

### `FormatCapability` (`[Flags]` enum)

| Flag | Meaning |
|---|---|
| `None` | Default. |
| `VariableResolution` | Supports any width/height (vs. fixed-size formats like Atari Degas 320×200). |
| `MonochromeOnly` | 1bpp formats (XBM, WBMP, fax G3/G4). |
| `IndexedOnly` | Always palette-based (Koala, GIF). |
| `HasDedicatedOptimizer` | A `Crush.<Format>` optimizer exists in the parent repo. |
| `MultiImage` | Multiple sub-images per file (TIFF pages, ICO entries, animated GIF/APNG, etc.). |

### `ImageInfo` (readonly record struct)

```csharp
public readonly record struct ImageInfo(
  int Width,
  int Height,
  int BitsPerPixel,
  string? ColorMode  = null,
  string? Compression = null,
  int FrameCount     = 1
);
```

Lightweight metadata returned by `FormatEntry.ReadImageInfo` for formats that expose a fast metadata path. Avoids decoding pixel data when you only need dimensions.

## How auto-discovery works

The `FileFormat.Registry.Generator` Roslyn source generator scans every referenced assembly at compile time for types implementing `IImageFormatReader<TSelf>`, `IImageFormatWriter<TSelf>`, `IImageToRawImage<TSelf>`, `IImageFromRawImage<TSelf>`, and `IMultiImageFileFormat<TSelf>`. It reads the type's `[FormatMagicBytes]`, `[FormatDetectionPriority]`, and `[FormatMimeType]` attributes, then emits:

1. The `ImageFormat` enum (one entry per discovered format).
2. A `FormatRegistration.RegisterAll()` partial method that wires up function pointers to the format's `FromBytes`/`FromSpan`/`ToBytes`/`ToRawImage`/`FromRawImage` static methods.

There is **no runtime reflection**. Adding a new format is purely additive: drop a new `FileFormat.<Name>` project in, ship one more DLL, and the next build extends the enum and registers the format with no code changes elsewhere.

## Stream detection internals

`DetectFromBytes` walks a single priority-sorted table where:

1. Formats with custom `MatchesSignature(ReadOnlySpan<byte>)` logic run first (they can return `true`/`false`/`null`; `null` means "not enough info, try other matchers"). Used for JPEG (`0xFF 0xD8 0xFF` followed by a marker from a specific set), AVIF (`ftyp` brand inside an MP4 box), JPEG 2000, etc.
2. Magic-byte signatures are checked in `(DetectionPriority, Format-name)` order. Lower numeric priority wins (so `[FormatDetectionPriority(0)]` runs before the default `100`).

## MIME types

MIME types come from `[FormatMimeType("image/png", "image/x-png", ...)]` attributes on each `FileFormat.<Name>.<Name>File` type. The first entry is the primary; subsequent entries are aliases (case-insensitive on lookup). Annotations are additive — long-tail formats without `[FormatMimeType]` simply have an empty `MimeTypes` array and `PrimaryMimeType == "application/octet-stream"`. Contributions adding more annotations are welcome.

## Supported formats

**540+ formats.** The tables below cover formats most consumers will care about; the complete list is enumerable at runtime via `FormatRegistry.AllFormats`.

### Modern / web

| Format | Read | Write | Multi-image | MIME | Reference |
|---|---|---|---|---|---|
| PNG       | ✓ | ✓ |   | `image/png`   | [W3C PNG](https://www.w3.org/TR/png/) |
| JPEG      | ✓ | ✓ |   | `image/jpeg`  | [ITU T.81](https://www.itu.int/rec/T-REC-T.81) |
| GIF       | ✓ |   | ✓ | `image/gif`   | [GIF89a spec](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| BMP       | ✓ | ✓ |   | `image/bmp`   | [MS DIB ref](https://learn.microsoft.com/windows/win32/gdi/bitmap-storage) |
| TIFF      | ✓ | ✓ | ✓ | `image/tiff`  | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| WebP      | ✓ | ✓ |   | `image/webp`  | [WebP spec](https://developers.google.com/speed/webp/docs/riff_container) |
| AVIF      | ✓ | ✓ |   | `image/avif`  | [AV1 Image File Format](https://aomediacodec.github.io/av1-avif/) |
| HEIF/HEIC | ✓ |   |   | `image/heic`  | [ISO/IEC 23008-12](https://nokiatech.github.io/heif/) |
| APNG      | ✓ | ✓ | ✓ | `image/apng`  | [W3C APNG](https://wiki.mozilla.org/APNG_Specification) |
| MNG       | ✓ |   | ✓ | `video/x-mng` | [MNG spec](http://www.libpng.org/pub/mng/spec/) |
| QOI       | ✓ | ✓ |   | `image/qoi`   | [QOI spec](https://qoiformat.org/qoi-specification.pdf) |
| BPG       | ✓ |   |   | —             | [BPG](https://bellard.org/bpg/) |
| FLIF      | ✓ |   |   | —             | [FLIF](https://flif.info/) |
| JPEG XL   | ✓ |   |   | `image/jxl`   | [ISO/IEC 18181](https://jpeg.org/jpegxl/) |
| JPEG 2000 | ✓ |   |   | `image/jp2`   | [ISO/IEC 15444](https://jpeg.org/jpeg2000/) |
| JPEG XR   | ✓ |   |   | `image/jxr`   | [ITU T.832](https://www.itu.int/rec/T-REC-T.832) |
| JPEG-LS   | ✓ |   |   | —             | [ITU T.87](https://www.itu.int/rec/T-REC-T.87) |
| JBIG / JBIG2 | ✓ |   |   | —          | [ISO/IEC 14492](https://www.itu.int/rec/T-REC-T.88) |
| DjVu      | ✓ |   |   | `image/vnd.djvu` | [DjVu spec](https://djvu.org/) |

### Lossless / scientific / HDR

| Format | Read | Write | MIME | Reference |
|---|---|---|---|---|
| Farbfeld     | ✓ | ✓ | `image/x-farbfeld`          | [Farbfeld](https://tools.suckless.org/farbfeld/) |
| Netpbm (PBM/PGM/PPM/PAM/P7) | ✓ | ✓ | `image/x-portable-anymap` | [Netpbm](http://netpbm.sourceforge.net/doc/) |
| PFM (Portable FloatMap) | ✓ | ✓ | `image/x-portable-floatmap` | [PFM](https://www.pauldebevec.com/Research/HDR/PFM/) |
| HDR (Radiance .hdr / RGBE) | ✓ | ✓ | `image/vnd.radiance` | [Radiance](https://www.radiance-online.org/) |
| OpenEXR      | ✓ | ✓ | `image/x-exr`           | [OpenEXR](https://openexr.com/) |
| DPX          | ✓ | ✓ | —                       | [SMPTE 268M](https://www.in70mm.com/news/2003/dpx_v2/index.htm) |
| Cineon       | ✓ | ✓ | —                       | [Cineon](https://www.kennethmoreland.com/color-maps/cineon.pdf) |
| FITS         | ✓ |   | —                       | [NASA FITS](https://fits.gsfc.nasa.gov/) |
| Analyze 7.5  | ✓ |   | —                       | [Mayo Clinic Analyze](https://eeg.sourceforge.net/ANALYZE75.pdf) |
| NIfTI / Nifti | ✓ |  | —                       | [NIfTI](https://nifti.nimh.nih.gov/) |
| MetaImage (.mhd/.mha) | ✓ | | —                | [ITK MetaImage](https://itk.org/Wiki/ITK/MetaIO) |
| NRRD         | ✓ |   | —                       | [NRRD](http://teem.sourceforge.net/nrrd/format.html) |
| MRC2014      | ✓ |   | —                       | [CCP-EM MRC](https://www.ccpem.ac.uk/mrc_format/mrc2014.php) |
| DICOM        | ✓ |   | `application/dicom`     | [DICOM](https://www.dicomstandard.org/) |
| ENVI         | ✓ |   | —                       | [ENVI hdr](https://www.l3harrisgeospatial.com/docs/enviheaderfiles.html) |
| VICAR        | ✓ |   | —                       | [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf) |
| PDS (NASA Planetary) | ✓ | | —                | [PDS](https://pds.nasa.gov/) |

### Professional / authoring

| Format | Read | Write | MIME | Reference |
|---|---|---|---|---|
| Photoshop PSD     | ✓ | ✓ | `image/vnd.adobe.photoshop` | [Adobe PSD](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |
| Photoshop PSB     | ✓ |   | —                           | (large PSD variant) |
| Krita KRA         | ✓ | ✓ | `application/x-krita`       | [Krita file format](https://docs.krita.org/) |
| OpenRaster ORA    | ✓ | ✓ | `image/openraster`          | [OpenRaster](https://www.openraster.org/) |
| GIMP XCF          | ✓ |   | —                           | [XCF](https://gitlab.gnome.org/GNOME/gimp/blob/master/devel-docs/xcf.txt) |
| MagicaVoxel VOX   | ✓ |   | —                           | [VOX](https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox.txt) |
| WMF / EMF         | ✓ |   | `image/wmf` / `image/emf`   | [WMF](https://learn.microsoft.com/openspecs/windows_protocols/ms-wmf/) |
| EPS               | ✓ |   | `application/postscript`    | [Adobe EPS](https://web.archive.org/web/20171109025324/https://www.adobe.com/products/postscript/pdfs/PLRM.pdf) |
| PDF (image extraction) | ✓ | | `application/pdf`         | [PDF 1.7](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf) |
| PE EXE/DLL (resource extraction) | ✓ | | —              | [PE/COFF](https://learn.microsoft.com/windows/win32/debug/pe-format) |
| VIPS              | ✓ |   | —                           | [libvips](https://www.libvips.org/) |
| SoftImage / Maya IFF | ✓ | | —                           | (3D renderer outputs) |

### GPU textures / 3D

| Format | Read | Write | Reference |
|---|---|---|---|
| DDS (DirectDraw Surface) | ✓ | ✓ | [DDS file ref](https://learn.microsoft.com/windows/win32/direct3ddds/dx-graphics-dds) |
| KTX / KTX2               | ✓ | ✓ | [Khronos KTX](https://registry.khronos.org/KTX/specs/) |
| PVR (PowerVR)            | ✓ | ✓ | [Imagination PVR](https://docs.imgtec.com/PVR-File-Format-Specification/) |
| ASTC                     | ✓ | ✓ | [ARM ASTC](https://github.com/ARM-software/astc-encoder) |
| PKM (ETC1/ETC2)          | ✓ | ✓ | [PKM](https://github.com/g-truc/gli) |
| VTF (Valve Texture)      | ✓ | ✓ | [VDC: VTF](https://developer.valvesoftware.com/wiki/Valve_Texture_Format) |
| BLP (Blizzard)           | ✓ |   | (WoW/SC2 textures) |
| FSH (EA Sports)          | ✓ | ✓ | [FSH](https://wiki.simtropolis.com/wiki/FSH) |
| WAD / WAD2 / WAD3        | ✓ | ✓ | [Quake WAD](https://quakewiki.org/wiki/Quake_file_formats) |
| MipTex (Quake/HL MDL)    | ✓ | ✓ | (Quake1, HL1 BSP) |
| Block decoders included  | — | — | BC1–BC7, ETC1/ETC2, ASTC LDR, PVRTC |

### Animation / multi-image

| Format | Read | Write | Multi-image | Reference |
|---|---|---|---|---|
| Animated GIF | ✓ |   | ✓ | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| APNG         | ✓ | ✓ | ✓ | [APNG](https://wiki.mozilla.org/APNG_Specification) |
| MNG          | ✓ |   | ✓ | [MNG](http://www.libpng.org/pub/mng/spec/) |
| FLI / FLC    | ✓ | ✓ | ✓ | [FLIC](https://www.compuphase.com/flic.htm) |
| Multi-page TIFF | ✓ | ✓ | ✓ | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| BigTIFF      | ✓ |   | ✓ | [BigTIFF](https://www.awaresystems.be/imaging/tiff/bigtiff.html) |
| DCX (multi-page PCX) | ✓ |  | ✓ | (Intel WinFax archive) |
| MPO (multi-pic JPEG) | ✓ | ✓ | ✓ | [CIPA DC-007](https://www.cipa.jp/std/documents/e/DC-007_E.pdf) |
| ICNS (Apple icons)   | ✓ |  | ✓ | [Apple icns](https://en.wikipedia.org/wiki/Apple_Icon_Image_format) |

### Icons / cursors / fonts

| Format | Read | Write | MIME | Reference |
|---|---|---|---|---|
| ICO (Windows icon)     | ✓ | ✓ | `image/vnd.microsoft.icon` | [ICO file ref](https://en.wikipedia.org/wiki/ICO_(file_format)) |
| CUR (Windows cursor)   | ✓ | ✓ | `image/vnd.microsoft.icon` | [CUR file ref](https://en.wikipedia.org/wiki/ICO_(file_format)#CUR_(format)) |
| ANI (animated cursor)  | ✓ | ✓ | —                          | [ANI](https://en.wikipedia.org/wiki/ANI_(file_format)) |
| ICNS (Apple)           | ✓ |   | —                          | (see above) |
| Xcursor (X11)          | ✓ |   | —                          | [Xcursor](https://www.x.org/releases/X11R7.7/doc/man/man3/Xcursor.3.xhtml) |
| SunIcon                | ✓ |   | —                          | (X bitmap variant) |
| MS FONT                | ✓ |   | —                          | [.FNT format](https://learn.microsoft.com/typography/opentype/spec/) |

### Document / fax

Pure raster + CCITT G3/G4 codecs. **44 fax variant formats** are supported (see `FormatRegistry.AllFormats` for the full list).

| Format | Read | Write | Reference |
|---|---|---|---|
| Fax G3 / Fax G4 | ✓ | ✓ | [ITU T.4 / T.6](https://www.itu.int/rec/T-REC-T.4) |
| WSQ (FBI fingerprint) | ✓ |  | [WSQ](https://www.fbibiospecs.cjis.gov/Document/Get?fileName=WSQ_Gray-scale_Specification_Version_3_1_Final.pdf) |
| Common fax containers | ✓ | ✓ | AccessFax, AdTechFax, BfxBitware, BrotherFax, CanonNavFax, EverexFax, FaxMan, FremontFax, GammaFax, HayesJtfax, ImagingFax, KofaxKfx, MobileFax, OazFax, OlicomFax, RicohFax, SciFax, SmartFax, TeliFax, Tg4, VentaFax, WinFax, WorldportFax, BrooktroutFax, EdmicsC4, AttGroup4 |
| Symbian MBM | ✓ | ✓ | (Symbian OS multi-bitmap) |

### RAW camera

| Format | Read | Write | Reference |
|---|---|---|---|
| Adobe DNG       | ✓ |   | [DNG spec](https://www.adobe.com/products/photoshop/extend.html) |
| Canon CR2       | ✓ |   | (lossless JPEG, slice reassembly) |
| Canon CR3 (partial) | ✓ |   | (HEIF container) |
| Nikon NEF       | ✓ |   | (compressed, dual Huffman) |
| Sony ARW2       | ✓ |   | (7-bit delta) |
| Olympus ORF     | ✓ |   | — |
| Panasonic RW2   | ✓ |   | — |

### Other notable

TGA / Targa, PCX, SGI / Iris, Sun Raster, X PixMap (XPM), X BitMap (XBM), Wireless Bitmap (WBMP), AAI (DuneHD), HRZ (slow-scan TV), CMU bitmap, GEM/GTM (Atari), ALDUS PageMaker, Macromedia FreeHand, Pixar PXR, AldusPagemaker, GD2 (libgd), DPX (motion picture), MIFF (ImageMagick), ECW (Enhanced Compression Wavelet), JNG (JPEG Network Graphics), VIFF (Khoros), RLA / RPF (Wavefront), ART (PFS), AliasPix.

### Vintage computing (~ 200 formats)

The package supports virtually every screen-dump and paint-program output ever shipped on a home/personal computer. Discoverable via `FormatRegistry.AllFormats`; selection of platforms below.

- **Apple**: Apple II / IIgs SHR / DHR / 16-color, AppleICN, AppleColorSPF, AppleSPF, MacPaint, PICT
- **Atari**: Degas / Degas Elite, NeoChrome, AtariPaintworks, CrackArt, Spectrum 512 (& Compressed/Smoosh), QuantumPaint, Sinbad Slideshow, FullscreenKit, PabloPaint, Stad, Calamus, ArtDirector, MegaPaint, GfaRaytrace, plus IFF and ZX0/ZXSP variants — 30+ formats
- **Commodore**: C64 (Koala, Doodle, Multicolor, Hires, AdvancedArt, AmicaPaint, GunPaint, FunPainter, DrazPaint, GigaPaint, Artist64, FacePainter, FunGraphicsMachine, GoDot, HiresC64, EggPaint, CDU-Paint, RainbowPainter, KoalaCompressed, Bfli, Vidcom64, Picasso64, MicroIllustrator, AdvancedArtStudio, RunPaint, InterPaint, PrintfoxPagefox, Spectrum512), C128, Plus/4, VIC-20, Amiga IFF / ILBM / ANIM / ACBM / DEEP / RGB8 / RGBN / PBM
- **Sinclair Spectrum**: ZxSpectrum (SCR), ZxNext, ZxTimex, ZxUlaPlus, ZxMulticolor, ZxBorderMulticolor, ZxPaintbrush, ZxArtStudio, Spectrum512Smoosh — 25+ formats
- **MSX**: MsxScreen2/5/7/8/10/12, MsxSc4, MsxSc8, MsxView — 15+ formats
- **Amstrad CPC**: AmstradCpc, AmstradCpcPlus, AmstradOcp, FontasyGrafik
- **Sharp**: SharpMz, X1Pal, SharpX68k
- **Acorn / BBC**: Acorn (Sprite), BbcMicroBeeb, BbcMicroAdvanced, RiscOsSprite
- **Sega**: Genesis/Mega Drive tile, Master System tile, Game Gear, Genesis SJ1
- **Nintendo**: GameBoy tile, GameBoyColor, GbaTile, NesChr, SnesTile, NintendoDsT (NDS texture), N64 SAI/TM, NeoGeoSprite, NeoGeoPocket, VirtualBoyTile
- **Other 8/16-bit**: TI bitmap, HP Grob, EpaBios (calculator), CiscoIp, PocketPc2bp, Thomson, Commodore PET, FM Towns, PC-88, Enterprise128, Atari 7800, Atari 2600, TRS-80, Dragon, Jupiter Ace, ZX81, Electronika, Vector06c, Vidcom64, Picasso64
- **Japanese formats**: Mag, Pi, Q0, MakichanGraph
- **Mobile/embedded**: NokiaLogo, NokiaNlm, NokiaGroupGraphics, SiemensBmx, PsionPic
- **HP**: HpBufImage, HpForth (HP48), HpGrob

### Get the complete list at runtime

```csharp
foreach (var entry in FormatRegistry.AllFormats.OrderBy(e => e.Name))
  Console.WriteLine(
    $"{entry.Name,-30} {entry.PrimaryExtension,-10} " +
    $"R={(entry.SupportsRead?'Y':'-')} W={(entry.SupportsWrite?'Y':'-')} " +
    $"M={(entry.SupportsMultiImage?'Y':'-')} {entry.PrimaryMimeType}");
```

## Limitations

- **Lossy alpha** — for codecs we built ourselves, lossy modes discard alpha. The pure-C# WebP VP8 lossy encoder is keyframe-only and discards alpha; use `ImageFormat.WebP` with a lossless source or write VP8L manually.
- **Codec subsets** — HEIF/AVIF/BPG decoders are I-frame only, single tile, YCbCr 4:2:0 8-bit. JPEG XL supports modular mode only (VarDCT lossy is deferred). Camera RAW supports DNG lossless JPEG, Canon CR2, Nikon NEF, Sony ARW2; other manufacturer-specific compressions are future work.
- **Read-only authoring formats** — PSD, XCF, PSB, ICNS, Xcursor, ECW, DjVu, JBIG2, FLIF (writers exist for some, but full spec-compliant write support is deferred).
- **JPEG chroma 4:2:2** — `BitMiracle.LibJpeg.NET` does not support 4:2:2; only 4:4:4 and 4:2:0 are encoded.
- **PDF / PE** — image extraction only. PDF rendering, page composition, vector graphics, and PE writing are out of scope.
- **Bundle size** — `~3.7 MB` (540 small DLLs). If you only need a few formats, future per-format NuGet packages may be published.
- **TFM** — targets `net8.0`. Older runtimes are not supported.

## License

LGPL-3.0-or-later. See [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).

## Contributing

Issues and PRs welcome at <https://github.com/Hawkynt/PNGCrushCS>. Adding a new format is straightforward — see existing `FileFormat.<Name>` projects as templates. Adding a `[FormatMimeType("image/...")]` annotation to an existing format is a one-line PR.
