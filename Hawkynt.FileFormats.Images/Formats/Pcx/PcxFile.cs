using System;
using FileFormat.Core;

namespace FileFormat.Pcx;

/// <summary>In-memory representation of a PCX image.</summary>
[FormatDetectionPriority(999)]
[FormatMimeType("image/x-pcx", "image/vnd.zbrush.pcx", "image/pcx")]
public readonly record struct PcxFile : IImageFormatReader<PcxFile>, IImageToRawImage<PcxFile>, IImageFromRawImage<PcxFile>, IImageFormatWriter<PcxFile> {

  static string IImageFormatMetadata<PcxFile>.PrimaryExtension => ".pcx";
  static string[] IImageFormatMetadata<PcxFile>.FileExtensions => [".pcx", ".pcc", ".fcx"];
  static PcxFile IImageFormatReader<PcxFile>.FromSpan(ReadOnlySpan<byte> data) => PcxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PcxFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static byte[] IImageFormatWriter<PcxFile>.ToBytes(PcxFile file) => PcxWriter.ToBytes(file);

  /// <summary>
  /// Recognises a PCX, which states no more of a signature than one byte.
  /// </summary>
  /// <remarks>
  /// A leading 0x0A and a version under six is not much to go on, and it claimed a file that merely
  /// began that way — an AutoDesk drawing, whose thumbnail is a BMP a hundred bytes in. The two
  /// fields after the version rule it out at no cost: the encoding is none or run-length, and the
  /// depth is one of four values, where that file states nought for both.
  /// </remarks>
  static bool? IImageFormatMetadata<PcxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4
       && header[0] == 0x0A
       && header[1] <= 5
       && header[2] <= 1
       && header[3] is 1 or 2 or 4 or 8
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public PcxColorMode ColorMode { get; init; }
  public PcxPlaneConfig PlaneConfig { get; init; }

  /// <summary>The two colours the header names, or null where it names none.</summary>
  private static byte[]? _StatedPair(byte[]? palette) {
    if (palette is not { Length: >= 6 })
      return null;

    // Both entries the same is not a choice of colours; it is an unfilled header.
    var same = true;
    for (var i = 0; i < 3; ++i)
      if (palette[i] != palette[i + 3]) {
        same = false;
        break;
      }

    return same ? null : [palette[0], palette[1], palette[2], palette[3], palette[4], palette[5]];
  }

  public static RawImage ToRawImage(PcxFile file) {

    var mode = file.ColorMode;
    if (mode == PcxColorMode.Original)
      mode = file.BitsPerPixel switch {
        24 => PcxColorMode.Rgb24,
        8 => PcxColorMode.Indexed8,
        4 => PcxColorMode.Indexed4,
        _ => PcxColorMode.Monochrome
      };

    PixelFormat format;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (mode) {
      case PcxColorMode.Rgb24:
        format = PixelFormat.Rgb24;
        break;
      case PcxColorMode.Indexed8:
        format = PixelFormat.Indexed8;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case PcxColorMode.Indexed4:
        format = PixelFormat.Indexed4;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case PcxColorMode.Monochrome:
        format = PixelFormat.Indexed1;

        // The file states its two colours in the header, and they are not always paper and ink —
        // amber on black was a common enough choice. They were ignored here and a fixed pair used
        // instead; the stated pair is used now, and the fixed one only where the header says
        // nothing, which is what a file leaving both entries black amounts to.
        palette = _StatedPair(file.Palette) ?? [0, 0, 0, 255, 255, 255];
        paletteCount = 2;
        break;
      default:
        throw new ArgumentException($"Unsupported PcxColorMode: {mode}", nameof(file));
    }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData,
      Palette = palette,
      PaletteCount = paletteCount
    };
  }

  public static PcxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    image = image.EnsureAnyFormat(
      PixelFormat.Rgb24, PixelFormat.Indexed8, PixelFormat.Indexed4, PixelFormat.Indexed1);

    PcxColorMode colorMode;
    int bpp;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (image.Format) {
      case PixelFormat.Rgb24:
        colorMode = PcxColorMode.Rgb24;
        bpp = 24;
        break;
      case PixelFormat.Indexed8:
        colorMode = PcxColorMode.Indexed8;
        bpp = 8;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed4:
        colorMode = PcxColorMode.Indexed4;
        bpp = 4;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed1:
        colorMode = PcxColorMode.Monochrome;
        bpp = 1;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for PCX: {image.Format}", nameof(image));
    }

    return new PcxFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = image.PixelData,
      Palette = palette,
      PaletteColorCount = paletteCount,
      ColorMode = colorMode,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };
  }
}
