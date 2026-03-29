using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pcx;

/// <summary>In-memory representation of a PCX image.</summary>
[FormatDetectionPriority(999)]
public sealed class PcxFile : IImageFileFormat<PcxFile> {

  static string IImageFileFormat<PcxFile>.PrimaryExtension => ".pcx";
  static string[] IImageFileFormat<PcxFile>.FileExtensions => [".pcx", ".pcc", ".fcx"];
  static FormatCapability IImageFileFormat<PcxFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static PcxFile IImageFileFormat<PcxFile>.FromFile(FileInfo file) => PcxReader.FromFile(file);
  static PcxFile IImageFileFormat<PcxFile>.FromBytes(byte[] data) => PcxReader.FromBytes(data);
  static PcxFile IImageFileFormat<PcxFile>.FromStream(Stream stream) => PcxReader.FromStream(stream);
  static byte[] IImageFileFormat<PcxFile>.ToBytes(PcxFile file) => PcxWriter.ToBytes(file);

  static bool? IImageFileFormat<PcxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == 0x0A && header[1] <= 5
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public PcxColorMode ColorMode { get; init; }
  public PcxPlaneConfig PlaneConfig { get; init; }

  public static RawImage ToRawImage(PcxFile file) {
    ArgumentNullException.ThrowIfNull(file);

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
        palette = [0, 0, 0, 255, 255, 255];
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
