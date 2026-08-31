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

[`../Formats.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Formats.md) is the broad repository cross-reference. It is maintained by hand and can lag the registry, so use `FormatRegistry` when exact current read/write state matters.

### Common and modern formats

| Format | Extensions | Read | Write | Multi-image | MIME | Reference |
| --- | --- | :---: | :---: | :---: | --- | --- |
| [PNG](https://en.wikipedia.org/wiki/PNG) | `.png` | ✅ | ✅ | — | `image/png` | [W3C PNG](https://www.w3.org/TR/png-3/) |
| [JPEG](https://en.wikipedia.org/wiki/JPEG) | `.jpg`, `.jpeg`, `.jfif`, … | ✅ | ✅ | — | `image/jpeg` | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [GIF](https://en.wikipedia.org/wiki/GIF) | `.gif` | ✅ | ✅ | ✅ | `image/gif` | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| [BMP](https://en.wikipedia.org/wiki/BMP_file_format) | `.bmp`, `.dib` | ✅ | ✅ | — | `image/bmp` | [Microsoft bitmap storage](https://learn.microsoft.com/windows/win32/gdi/bitmap-storage) |
| [TIFF](https://en.wikipedia.org/wiki/TIFF) | `.tif`, `.tiff` | ✅ | ✅ | ✅ | `image/tiff` | [TIFF 6.0](https://www.adobe.io/open/standards/TIFF.html) |
| [WebP](https://en.wikipedia.org/wiki/WebP) | `.webp` | ✅ | ✅ | ✅ | `image/webp` | [WebP RIFF container](https://developers.google.com/speed/webp/docs/riff_container) |
| [AVIF](https://en.wikipedia.org/wiki/AVIF) | `.avif` | ⚠️ | — | — | `image/avif` | [AOMedia AVIF](https://aomediacodec.github.io/av1-avif/) |
| [HEIF / HEIC](https://en.wikipedia.org/wiki/High_Efficiency_Image_File_Format) | `.heif`, `.heic` | ⚠️ | — | ✅ | `image/heic` | [Nokia HEIF](https://nokiatech.github.io/heif/) |
| [APNG](https://en.wikipedia.org/wiki/APNG) | `.apng`, `.png` | ✅ | ✅ | ✅ | `image/apng` | [APNG specification](https://wiki.mozilla.org/APNG_Specification) |
| [MNG](https://en.wikipedia.org/wiki/Multiple-image_Network_Graphics) | `.mng` | ✅ | ⚠️ | ✅ | `video/x-mng` | [MNG specification](http://www.libpng.org/pub/mng/spec/) |
| [QOI](https://en.wikipedia.org/wiki/QOI_(image_format)) | `.qoi` | ✅ | ✅ | — | `image/qoi` | [QOI specification](https://qoiformat.org/qoi-specification.pdf) |
| [JPEG XL](https://en.wikipedia.org/wiki/JPEG_XL) | `.jxl` | ⚠️ | ⚠️ | — | `image/jxl` | [JPEG XL](https://jpeg.org/jpegxl/) |
| [JPEG 2000](https://en.wikipedia.org/wiki/JPEG_2000) | `.jp2`, `.j2k`, … | ✅ | ✅ | — | `image/jp2` | [JPEG 2000](https://jpeg.org/jpeg2000/) |
| [JPEG XR](https://en.wikipedia.org/wiki/JPEG_XR) | `.jxr`, `.wdp`, `.hdp` | ⚠️ | ⚠️ | — | `image/jxr` | [ITU-T T.832](https://www.itu.int/rec/T-REC-T.832) |
| [BPG](https://en.wikipedia.org/wiki/Better_Portable_Graphics) | `.bpg` | ✅ | — | — | — | [Fabrice Bellard's BPG](https://bellard.org/bpg/) |
| [FLIF](https://en.wikipedia.org/wiki/Free_Lossless_Image_Format) | `.flif` | ✅ | — | — | — | [FLIF project](https://flif.info/) |

`⚠️` means a material subset or interoperability limitation exists. AVIF container handling is present, but real AV1 pixel payloads are deliberately not decoded by the current nonconforming AV1 codec and there is no registered AV1 encoder. HEIF/HEIC now decodes directly coded HEVC Main-profile intra-picture items through the managed H.265 codec and exposes additional image items through the multi-image contract, but unsupported HEVC profiles/features are rejected and no general HEVC authoring path is registered. MNG writing targets the conforming MNG-VLC subset rather than every MNG feature. JPEG XL container/header handling is useful, but its current pixel codec is not interoperable with arbitrary libjxl files. JPEG XR recognizes real containers but deliberately refuses the current incorrect real-file pixel output. JPEG 2000 writing now uses the conforming managed baseline Tier-1/Tier-2 path rather than the former private packet grammar.

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

### Vintage computing (~200 formats)

The package contains roughly 200 screen-dump, paint-program, tile, sprite, icon, and platform-native image formats from home computers, consoles, and early personal-computing systems. The table is grouped by platform family so the NuGet landing page remains usable; [`../Formats.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Formats.md) and `FormatRegistry.AllFormats` provide the individual entries.

| Platform / family | Representative registered formats | Read | Write | Reference |
| --- | --- | :---: | :---: | --- |
| Apple II / IIgs / classic Mac | Apple II, IIgs SHR/DHR/16-color, AppleICN, AppleColorSPF, AppleSPF, MacPaint, PICT | ✅ | ⚠️ | [Apple II graphics](https://en.wikipedia.org/wiki/Apple_II_graphics), [MacPaint](https://en.wikipedia.org/wiki/MacPaint), [PICT](https://en.wikipedia.org/wiki/PICT) |
| Atari 8-bit / ST | Degas / Degas Elite, NeoChrome, AtariPaintworks, CrackArt, Spectrum 512 variants, QuantumPaint, Stad, Calamus, ArtDirector, MegaPaint, GfaRaytrace | ✅ | ⚠️ | [Atari ST](https://en.wikipedia.org/wiki/Atari_ST), [Spectrum 512](https://en.wikipedia.org/wiki/Spectrum_512) |
| Commodore 64 / 128 / Plus/4 / VIC-20 | Koala, Doodle, Multicolor, Hires, AdvancedArt, AmicaPaint, GunPaint, FunPainter, DrazPaint, GigaPaint, Artist64, FacePainter, GoDot, Printfox/Pagefox, and many others | ✅ | ⚠️ | [Commodore 64 graphics](https://en.wikipedia.org/wiki/Commodore_64#Graphics) |
| Amiga | IFF, ILBM, ANIM, ACBM, DEEP, RGB8, RGBN, PBM | ✅ | ⚠️ | [IFF](https://en.wikipedia.org/wiki/Interchange_File_Format), [ILBM](https://en.wikipedia.org/wiki/ILBM) |
| Sinclair ZX Spectrum / Timex / Next | SCR, ZxNext, ZxTimex, ZxUlaPlus, ZxMulticolor, ZxBorderMulticolor, ZxPaintbrush, ZxArtStudio | ✅ | ⚠️ | [ZX Spectrum graphic modes](https://en.wikipedia.org/wiki/ZX_Spectrum_graphic_modes) |
| MSX | Screen 2/5/7/8/10/12, SC4, SC8, MSX View | ✅ | ⚠️ | [MSX](https://en.wikipedia.org/wiki/MSX) |
| Amstrad CPC / CPC Plus | AmstradCpc, AmstradCpcPlus, AmstradOcp, FontasyGrafik | ✅ | ⚠️ | [Amstrad CPC](https://en.wikipedia.org/wiki/Amstrad_CPC) |
| Sharp systems | Sharp MZ, X1Pal, Sharp X68000 | ✅ | ⚠️ | [Sharp MZ](https://en.wikipedia.org/wiki/Sharp_MZ), [X68000](https://en.wikipedia.org/wiki/X68000) |
| Acorn / BBC / RISC OS | Acorn Sprite, BbcMicroBeeb, BbcMicroAdvanced, RiscOsSprite | ✅ | ⚠️ | [BBC Micro](https://en.wikipedia.org/wiki/BBC_Micro), [RISC OS](https://en.wikipedia.org/wiki/RISC_OS) |
| Sega consoles | Genesis / Mega Drive tiles, Master System tiles, Game Gear, Genesis SJ1 | ✅ | ⚠️ | [Mega Drive / Genesis](https://en.wikipedia.org/wiki/Sega_Genesis), [Master System](https://en.wikipedia.org/wiki/Master_System) |
| Nintendo / SNK consoles | Game Boy / Game Boy Color, GBA tiles, NES CHR, SNES tiles, Nintendo DS textures, N64 SAI/TM, Neo Geo sprites / Pocket, Virtual Boy tiles | ✅ | ⚠️ | [NES PPU](https://en.wikipedia.org/wiki/Picture_Processing_Unit), [Game Boy](https://en.wikipedia.org/wiki/Game_Boy), [Super NES](https://en.wikipedia.org/wiki/Super_Nintendo_Entertainment_System) |
| Other 8/16-bit systems | TI bitmap, HP GROB, EPA BIOS, CiscoIp, PocketPc2bp, Thomson, PET, FM Towns, PC-88, Enterprise 128, Atari 2600/7800, TRS-80, Dragon, Jupiter Ace, ZX81, Vector-06C | ✅ | ⚠️ | [Home computer](https://en.wikipedia.org/wiki/Home_computer) |
| Japanese interchange / paint formats | MAG, Pi, Q0, Makichan Graph | ✅ | ⚠️ | [MAG image format overview](https://en.wikipedia.org/wiki/MAG_(file_format)) |
| Mobile / embedded | NokiaLogo, NokiaNlm, NokiaGroupGraphics, SiemensBmx, PsionPic | ✅ | ⚠️ | [Nokia logo formats context](https://en.wikipedia.org/wiki/Nokia_Logo_Manager) |
| HP calculators / workstations | HpBufImage, HpForth / HP48, HpGrob | ✅ | ⚠️ | [HP 48 series](https://en.wikipedia.org/wiki/HP_48_series) |

`⚠️` in the grouped Write column means writer coverage varies between formats in that family; it does not mean every listed format has a partial writer. Query each `FormatEntry.SupportsWrite` for the exact current capability.

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

var (detected, replay) = FormatRegistry.DetectFromStreamRewound(networkStream);
RawImage? image = FormatRegistry.Read(replay);
```

### Read and normalize any image

```csharp
RawImage? image = FormatRegistry.Read(new FileInfo("anything.tga"));
if (image != null) {
  Console.WriteLine($"{image.Width}x{image.Height} {image.Format} HasAlpha={image.HasAlpha}");
  byte[] rgba = image.ToRgba32();
}
```

### Encode a `RawImage`

```csharp
var raw = new RawImage {
  Width = 256,
  Height = 256,
  Format = PixelFormat.Rgba32,
  PixelData = pixelBytes,
};

byte[]? png = FormatRegistry.Write(raw, ImageFormat.Png);
byte[]? webp = FormatRegistry.Write(raw, ImageFormat.WebP);
byte[]? qoi = FormatRegistry.Write(raw, ImageFormat.Qoi);

using var output = File.Create("out.bmp");
bool ok = FormatRegistry.Write(raw, ImageFormat.Bmp, output);
```

### Look up extensions and MIME types

```csharp
string ext = FormatRegistry.PrimaryExtension(ImageFormat.Jpeg);
var aliases = FormatRegistry.AllExtensions(ImageFormat.Jpeg);
string mime = FormatRegistry.PrimaryMimeType(ImageFormat.WebP);
var mimes = FormatRegistry.AllMimeTypes(ImageFormat.Png);
```

### Inspect and filter capabilities

```csharp
var roundTrippable = FormatRegistry.AllFormats
  .Where(e => e.SupportsRead && e.SupportsWrite)
  .OrderBy(e => e.Name);

var multiImage = FormatRegistry.AllFormats.Where(e => e.SupportsMultiImage);
var mimed = FormatRegistry.AllFormats.Where(e => e.MimeTypes.Length > 0);
```

### Cross-format conversion

```csharp
File.WriteAllBytes(
  "out.png",
  FormatRegistry.Write(FormatRegistry.Read(new FileInfo("in.tga"))!, ImageFormat.Png)!);
```

### Metadata without decoding pixels

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Jpeg);
ImageInfo? info = entry?.ReadImageInfo?.Invoke(File.ReadAllBytes("photo.jpg"));
if (info is { } meta)
  Console.WriteLine($"{meta.Width}x{meta.Height} @ {meta.BitsPerPixel}bpp ({meta.ColorMode})");
```

`ReadImageInfo` is `null` when a format has no fast metadata-only path; callers can fall back to `Read`.

### Multi-image formats

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Tiff);
if (entry?.SupportsMultiImage == true) {
  int pages = entry.GetImageCount!(new FileInfo("multi.tif"));
  for (var i = 0; i < pages; ++i) {
    RawImage? page = entry.LoadRawImageAtIndex!(new FileInfo("multi.tif"), i);
    // ...
  }

  var allPages = entry.LoadAllRawImages!(new FileInfo("multi.tif"));
}
```

## 🧭 Key types at a glance

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
| `PrimaryExtension` / `AllExtensions` | Canonical extension and aliases. |
| `PrimaryMimeType` / `AllMimeTypes` | Preferred MIME type and aliases. |
| `AllFormats` | Enumerate every registered format. |
| `SupportedReadFormats` / `SupportedWriteFormats` | Enumerate by capability. |

### `FormatEntry`

The source generator produces typed registry entries rather than using runtime reflection. The public record carries the format identity, names/extensions/MIME types, capabilities/signatures/detection priority, read/write delegates, optional metadata reader, and optional multi-image delegates.

Key computed properties are:

| Property | Meaning |
| --- | --- |
| `PrimaryMimeType` | First registered MIME type, otherwise `application/octet-stream`. |
| `SupportsRead` | A reader is registered. |
| `SupportsWrite` | A conversion from arbitrary `RawImage` is registered. |
| `SupportsMultiImage` | Image count/index/all-image delegates are registered. |

### `MagicSignature`

```csharp
public readonly record struct MagicSignature(
  byte[] Signature,
  int Offset,
  int MinHeaderLength);
```

Signatures are emitted from `[FormatMagicBytes(...)]`; `MinHeaderLength` prevents matchers from reading beyond the supplied header.

### `ImageFormat`

`ImageFormat` is generated at compile time. `Unknown = 0`; every discovered image format contributes another member. Consumers should not copy a hand-maintained enum list into application code—use the package's generated enum/registry.

### `RawImage`

```csharp
public sealed class RawImage {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required PixelFormat Format { get; init; }
  public required byte[] PixelData { get; init; }

  public byte[]? Palette { get; init; }
  public int PaletteCount { get; init; }
  public byte[]? AlphaTable { get; init; }

  public bool IsIndexed { get; }
  public bool HasAlpha { get; }

  public byte[] ToBgra32();
  public byte[] ToRgba32();
  public byte[] ToRgb24();

  public static int BytesPerPixel(PixelFormat format);
  public static int BitsPerPixel(PixelFormat format);
}
```

### `PixelFormat`

| Value | Layout | Bits |
| --- | --- | ---: |
| `Bgra32` | B, G, R, A | 32 |
| `Rgba32` | R, G, B, A | 32 |
| `Argb32` | A, R, G, B | 32 |
| `Rgb24` | R, G, B | 24 |
| `Bgr24` | B, G, R | 24 |
| `Gray8` | grayscale | 8 |
| `Gray16` | 16-bit grayscale | 16 |
| `GrayAlpha16` | grayscale + alpha | 16 |
| `Indexed8` | palette index | 8 |
| `Indexed4` | packed palette index | 4 |
| `Indexed1` | packed palette index | 1 |
| `Rgba64` | 16-bit R/G/B/A | 64 |
| `Rgb48` | 16-bit R/G/B | 48 |
| `Rgb565` | 5/6/5 RGB | 16 |

### `FormatCapability`

`FormatCapability` contains format-level flags such as `HasDedicatedOptimizer` and `MultiImage`. Per-format geometry/palette/display restrictions are modeled separately through `VideoMode` rather than being flattened into capability bits.

### `VideoMode`

```csharp
public sealed record VideoMode(
  string Name,
  (IntegerRange Width, IntegerRange Height)[] Dimensions,
  IntegerRange[]? AllowedPaletteRanges = null,
  FixedPalette[]? AvailablePalettes = null,
  PixelAspectRatio? PixelAspectRatio = null,
  DisplayFilter DisplayFilter = DisplayFilter.None,
  string? Description = null);
```

A format declares its selectable modes through `IImageFormatMetadata<TSelf>.VideoModes`. Multiple resolutions sharing one palette profile stay in one mode; palette variants for the same dimensions belong in `AvailablePalettes`.

Representative declarations include:

```csharp
// Arbitrary-resolution full colour.
static VideoMode[] VideoModes => [
  new("Default", [(IntegerRange.Any, IntegerRange.Any)])
];

// Atari ST NeoChrome.
static VideoMode[] VideoModes => [
  new("Low resolution", [(320, 200)], [16]),
  new("Medium resolution", [(640, 200)], [4]),
  new("High resolution", [(640, 400)], [2]),
];

// CGA palette variants stay attached to the same geometry/profile.
static VideoMode[] VideoModes => [
  new("4-colour", [(320, 200)], [4],
      [_LowIntensity0, _HighIntensity0, _LowIntensity1, _HighIntensity1]),
  new("Monochrome", [(640, 200)], [2], [_MonochromePalette]),
];

// NES CHR can additionally describe pixel aspect and display filtering.
static VideoMode[] VideoModes => [
  new("Tilesheet (2bpp)",
      [(128, new IntegerRange(8, 8192, step: 8))],
      [new IntegerRange(2, 4)],
      [_NesMaster64],
      (8, 7),
      DisplayFilter.NtscComposite),
];
```

### `ImageInfo`

```csharp
public readonly record struct ImageInfo(
  int Width,
  int Height,
  int BitsPerPixel,
  string? ColorMode = null,
  string? Compression = null,
  int FrameCount = 1);
```

It is the lightweight metadata result for formats that can inspect dimensions/properties without a full pixel decode.

## 🏗️ Registry and detection architecture

`FileFormat.Registry.Generator` scans the compilation and referenced format assemblies at build time for the static image contracts and emits the `ImageFormat` enum plus registration code wired directly to the concrete static methods. There is no runtime reflection step.

Detection runs priority-ordered custom `MatchesSignature(ReadOnlySpan<byte>)` logic for formats that need more than fixed magic bytes, then the generated magic-signature table. `DetectFromFile` falls back to extension only after byte-level detection fails.

MIME types come from `[FormatMimeType(...)]`. The first annotation is the primary MIME type and later values are aliases; formats without a MIME annotation simply expose no registered MIME values rather than receiving guessed ones.

## 📚 Extended format-family reference

The exact current Read/Write/Multi-image state for every individual entry remains `FormatRegistry.AllFormats`. The tables here preserve the package's long-form inventory without freezing old capability counts or resurrecting obsolete states.

### Lossless / scientific / HDR long tail

| Family / format | Coverage note | Reference |
| --- | --- | --- |
| Farbfeld | simple 16-bit-per-channel lossless raster | [farbfeld](https://tools.suckless.org/farbfeld/) |
| Netpbm PBM/PGM/PPM/PAM/P7 | portable bitmap/graymap/pixmap/anymap family | [Netpbm](http://netpbm.sourceforge.net/doc/) |
| Analyze 7.5 | medical/scientific volume imagery | [Analyze 7.5](https://eeg.sourceforge.net/ANALYZE75.pdf) |
| MetaImage `.mhd` / `.mha` | ITK MetaIO image data | [MetaImage](https://itk.org/Wiki/ITK/MetaIO) |
| MRC2014 | electron microscopy/volume data | [CCP-EM MRC](https://www.ccpem.ac.uk/mrc_format/mrc2014.php) |
| DICOM | medical image/container paths | [DICOM](https://www.dicomstandard.org/) |
| ENVI | remote-sensing raster/header format | [ENVI header files](https://www.l3harrisgeospatial.com/docs/enviheaderfiles.html) |
| VICAR | NASA/JPL image format | [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf) |
| PDS | NASA Planetary Data System imagery | [PDS](https://pds.nasa.gov/) |

### Professional / authoring

| Family / format | Coverage note | Reference |
| --- | --- | --- |
| Photoshop PSD / PSB | Adobe Photoshop documents; exact writer state is registry-defined | [Adobe PSD/PSB](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |
| Krita KRA | Krita document container/image data | [Krita](https://docs.krita.org/) |
| OpenRaster ORA | layered raster interchange | [OpenRaster](https://www.openraster.org/) |
| GIMP XCF | GIMP native image document | [XCF specification](https://developer.gimp.org/core/standards/xcf/) |
| MagicaVoxel VOX | voxel scene/image data | [VOX format](https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox.txt) |
| WMF / EMF | Windows metafile/vector-rendering paths | [MS-WMF](https://learn.microsoft.com/openspecs/windows_protocols/ms-wmf/) |
| EPS / PostScript | raster-preview/extraction paths | [PostScript reference](https://www.adobe.com/jp/print/postscript/pdfs/PLRM.pdf) |
| PDF | image extraction rather than page renderer/editor | [ISO 32000 background](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/) |
| PE EXE/DLL | image/resource extraction, not executable editing | [PE/COFF](https://learn.microsoft.com/windows/win32/debug/pe-format) |
| VIPS | libvips native image format | [libvips](https://www.libvips.org/) |
| SoftImage / Maya IFF | authoring/renderer image outputs | [IFF](https://en.wikipedia.org/wiki/Interchange_File_Format) |

### GPU textures / 3D

| Format | Coverage note | Reference |
| --- | --- | --- |
| DDS | DirectDraw/DirectX textures | [DDS](https://learn.microsoft.com/windows/win32/direct3ddds/dx-graphics-dds) |
| KTX / KTX2 | Khronos texture containers | [KTX](https://registry.khronos.org/KTX/specs/) |
| PVR | PowerVR texture container | [PVR format](https://docs.imgtec.com/PVR-File-Format-Specification/) |
| ASTC | Adaptive Scalable Texture Compression | [ASTC encoder/spec resources](https://github.com/ARM-software/astc-encoder) |
| PKM / ETC1 / ETC2 | Ericsson texture compression container/data | [Khronos ETC](https://registry.khronos.org/OpenGL/extensions/OES/OES_compressed_ETC1_RGB8_texture.txt) |
| VTF | Valve Texture Format | [Valve VTF](https://developer.valvesoftware.com/wiki/Valve_Texture_Format) |
| BLP | Blizzard texture family | [BLP](https://wowdev.wiki/BLP) |
| FSH | EA Sports texture container | [FSH](https://wiki.simtropolis.com/wiki/FSH) |
| WAD/WAD2/WAD3, MipTex | Quake/Half-Life texture archives and embedded texture data | [Quake file formats](https://quakewiki.org/wiki/Quake_file_formats) |
| Block codecs | BC1–BC7, ETC1/ETC2, ASTC LDR, PVRTC helpers used by texture formats | [DirectX BC formats](https://learn.microsoft.com/windows/win32/direct3d11/texture-block-compression-in-direct3d-11) |

### Animation / multi-image

| Format | Coverage note | Reference |
| --- | --- | --- |
| GIF | animated frame/page access through the multi-image contract | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| APNG | animated PNG | [APNG spec](https://wiki.mozilla.org/APNG_Specification) |
| MNG | Multiple-image Network Graphics; writer is a documented subset | [MNG](http://www.libpng.org/pub/mng/spec/) |
| FLI/FLC | Autodesk Animator family | [FLIC](https://www.compuphase.com/flic.htm) |
| TIFF / BigTIFF | multi-page image families | [TIFF](https://www.adobe.io/open/standards/TIFF.html), [BigTIFF](https://www.awaresystems.be/imaging/tiff/bigtiff.html) |
| DCX | multi-page PCX/WinFax family | [DCX](https://fileformats.archiveteam.org/wiki/DCX) |
| MPO | multi-picture JPEG | [CIPA DC-007](https://www.cipa.jp/std/documents/e/DC-007_E.pdf) |
| ICNS | Apple icon resources with multiple representations | [ICNS](https://en.wikipedia.org/wiki/Apple_Icon_Image_format) |

### Icons / cursors / fonts

| Format | Coverage note | Reference |
| --- | --- | --- |
| ICO / CUR | Windows icons/cursors | [ICO/CUR](https://en.wikipedia.org/wiki/ICO_(file_format)) |
| ANI | animated Windows cursors | [ANI](https://en.wikipedia.org/wiki/ANI_(file_format)) |
| ICNS | Apple icon images | [ICNS](https://en.wikipedia.org/wiki/Apple_Icon_Image_format) |
| Xcursor | X11 cursor images | [Xcursor](https://www.x.org/releases/X11R7.7/doc/man/man3/Xcursor.3.xhtml) |
| SunIcon | Sun/X bitmap-family icon data | [XBM background](https://en.wikipedia.org/wiki/X_BitMap) |
| MS FONT / FNT | bitmap-font glyph imagery | [Windows font resources](https://learn.microsoft.com/windows/win32/menurc/font-resource) |

### Document / fax

The package includes CCITT Group 3/4 primitives plus numerous fax-container variants including AccessFax, AdTechFax, BfxBitware, BrotherFax, CanonNavFax, EverexFax, FaxMan, FremontFax, GammaFax, HayesJtfax, ImagingFax, KofaxKfx, MobileFax, OazFax, OlicomFax, RicohFax, SciFax, SmartFax, TeliFax, Tg4, VentaFax, WinFax, WorldportFax, BrooktroutFax, EdmicsC4 and AttGroup4. Exact writer state is per registry entry.

| Format/family | Reference |
| --- | --- |
| CCITT Fax Group 3 / Group 4 | [ITU-T T.4](https://www.itu.int/rec/T-REC-T.4) / [T.6](https://www.itu.int/rec/T-REC-T.6) |
| WSQ fingerprint imagery | [FBI WSQ specification](https://www.fbibiospecs.cjis.gov/Document/Get?fileName=WSQ_Gray-scale_Specification_Version_3_1_Final.pdf) |
| Symbian MBM | [Symbian bitmap background](https://en.wikipedia.org/wiki/Symbian) |

### RAW camera

| Format | Coverage note | Reference |
| --- | --- | --- |
| Adobe DNG | TIFF-derived digital negative | [DNG specification](https://helpx.adobe.com/camera-raw/digital-negative.html) |
| Canon CR2 | lossless-JPEG/slice-oriented paths | [CR2 background](https://en.wikipedia.org/wiki/Raw_image_format#Canon) |
| Canon CR3 | HEIF/ISOBMFF-derived subset | [CR3 background](https://en.wikipedia.org/wiki/Raw_image_format#Canon) |
| Nikon NEF | manufacturer RAW paths including compressed variants | [NEF background](https://en.wikipedia.org/wiki/Raw_image_format#Nikon) |
| Sony ARW2 | Sony RAW path including delta coding | [ARW background](https://en.wikipedia.org/wiki/Raw_image_format#Sony) |
| Olympus ORF | Olympus RAW | [ORF background](https://en.wikipedia.org/wiki/Raw_image_format#Olympus) |
| Panasonic RW2 | Panasonic RAW | [RW2 background](https://en.wikipedia.org/wiki/Raw_image_format#Panasonic) |

### Other notable formats

The long tail also includes TGA/Targa, PCX, SGI/Iris, Sun Raster, X PixMap (XPM), X BitMap (XBM), Wireless Bitmap (WBMP), AAI/DuneHD, HRZ slow-scan TV, CMU bitmap, GEM/GTM, PageMaker-related formats, Macromedia FreeHand, Pixar PXR, GD2/libgd, MIFF/ImageMagick, ECW, JNG, VIFF/Khoros, RLA/RPF/Wavefront, ART/PFS and AliasPix. `FormatRegistry.AllFormats` is the definitive individual list.

## 🧪 Detection, registration, and verification notes

- Registry generation is compile-time and typed; adding a format that implements the static contracts extends the generated enum/registration without runtime reflection.
- Custom signature matchers may return `true`, `false`, or `null` when a header is insufficient; fixed magic signatures run through the generated priority table.
- A format's MIME aliases are explicit metadata, not guesses derived from extensions.
- A registered reader is not evidence for a writer; `SupportsWrite` is derived from the actual conversion delegate.
- Writers should be validated against a specification, external implementation, or other independent evidence. Two project methods agreeing only with each other is not treated as sufficient conformance evidence.
- Exact fast-moving counts belong to the generated registry/build rather than repeated prose.

## 📚 API reference

<!-- API:BEGIN generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->

Every public and protected member of all 3231 types, generated from the built assembly and its XML documentation, is in [REFERENCE.md](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Images/REFERENCE.md).

<!-- API:END -->

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

- **Lossy advanced features** — VP8 lossy is keyframe-only; multi-pass rate control and token-partition threading are not implemented yet. Alpha IS preserved (the encoder writes an ALPH chunk on RGBA input; uncompressed method 0 — VP8L-encoded alpha is a future optimization).
- **Codec subsets** — HEIF/HEIC now resolves and decodes directly coded HEVC image items through the shared managed H.265 decoder; that path currently targets Main-profile intra-picture 8-bit 4:2:0 content and rejects unsupported HEVC profiles/features instead of fabricating pixels. AVIF container parsing exists, but real AV1 pixel decoding remains disabled until the AV1 entropy syntax is conforming. BPG remains an I-frame-oriented managed subset. **JPEG 2000** writing uses a deliberately narrow 8-bit Gray/RGB conforming baseline profile; unsupported optional coding modes are outside that authoring profile rather than encoded with private syntax. **JPEG XL**: container + SizeHeader + ImageMetadata + FrameHeader (ISO/IEC 18181-1 §3.6.2 / §3.6.3 / §3.6.5) are spec-conformant — the all_default fast path that most libjxl-encoded files use is fully supported, and the non-default conditional plumbing (orientation, bit_depth, num_extra_channels, extra_channel_info, color_encoding, tone_mapping, frame_type, encoding flag) is in place. Pixel codec (modular sub-codec body and VarDCT) is the remaining workstream — arbitrary real-world `.jxl` files will not decode their pixels yet, but signature, dimensions, and image-level metadata are extracted correctly. **JPEG XR** recognizes real containers but the current pixel decoder is known to reproduce the wrong image, so real-file pixels are deliberately refused until the T.832 codec is repaired. Camera RAW supports DNG lossless JPEG, Canon CR2, Nikon NEF, Sony ARW2; other manufacturer-specific compressions are future work.
- **Write coverage** — 344 of 547 formats implement `FromRawImage` and can encode an arbitrary image; `FormatRegistry.Write` returns `null` for the other 203. Those parse and re-serialize a file they read, but cannot author one from pixel data — this includes the authoring formats (PSD, XCF, PSB, ICNS, Xcursor, ECW, DjVu, JBIG2, FLIF) and most vintage/8-bit formats. Filter on `FormatEntry.SupportsWrite` rather than assuming.
- **PDF / PE** — image extraction only. PDF rendering, page composition, vector graphics, and PE writing are out of scope.
- **Bundle size** — `~4.9 MB`, four assemblies. There is no way to take only the formats you need; if that matters, per-format NuGet packages may be published in future.
- **TFM** — targets `net8.0`. Older runtimes are not supported.
- Coverage breadth is larger than conformance depth. Some historical formats have scarce or no public samples; registry presence is not a promise that every obscure producer variant has been verified.
- The current JPEG XL pixel path is not general libjxl interoperability; do not treat its internal round-trip as proof of arbitrary `.jxl` compatibility.
- [`../Formats.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Formats.md) is useful as a human cross-reference, but the runtime registry is authoritative for current read/write capability.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).
