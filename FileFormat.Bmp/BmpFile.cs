using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>In-memory representation of a BMP image.</summary>
[FormatMagicBytes([0x42, 0x4D])]
public sealed class BmpFile : IImageFileFormat<BmpFile> {

  static string IImageFileFormat<BmpFile>.PrimaryExtension => ".bmp";
  static string[] IImageFileFormat<BmpFile>.FileExtensions => [".bmp", ".dib", ".bga", ".rl4", ".rl8", ".vga", ".sys"];
  static FormatCapability IImageFileFormat<BmpFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static BmpFile IImageFileFormat<BmpFile>.FromFile(FileInfo file) => BmpReader.FromFile(file);
  static BmpFile IImageFileFormat<BmpFile>.FromBytes(byte[] data) => BmpReader.FromBytes(data);
  static BmpFile IImageFileFormat<BmpFile>.FromStream(Stream stream) => BmpReader.FromStream(stream);
  static byte[] IImageFileFormat<BmpFile>.ToBytes(BmpFile file) => BmpWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public BmpRowOrder RowOrder { get; init; }
  public BmpCompression Compression { get; init; }
  public BmpColorMode ColorMode { get; init; }

  public static RawImage ToRawImage(BmpFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var mode = file.ColorMode;
    if (mode == BmpColorMode.Original)
      mode = file.BitsPerPixel switch {
        24 => BmpColorMode.Rgb24,
        16 => BmpColorMode.Rgb16_565,
        8 when file.Palette != null => BmpColorMode.Palette8,
        8 => BmpColorMode.Grayscale8,
        4 => BmpColorMode.Palette4,
        _ => BmpColorMode.Palette1
      };

    PixelFormat format;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (mode) {
      case BmpColorMode.Rgb24:
        format = PixelFormat.Bgr24;
        break;
      case BmpColorMode.Rgb16_565:
        format = PixelFormat.Rgb565;
        break;
      case BmpColorMode.Palette8:
        format = PixelFormat.Indexed8;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Palette4:
        format = PixelFormat.Indexed4;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Palette1:
        format = PixelFormat.Indexed1;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Grayscale8:
        format = PixelFormat.Gray8;
        break;
      default:
        throw new ArgumentException($"Unsupported BmpColorMode: {mode}", nameof(file));
    }

    var bpp = file.BitsPerPixel;
    var stride = bpp >= 8 ? file.Width * (bpp / 8) : bpp == 4 ? (file.Width + 1) / 2 : (file.Width + 7) / 8;
    var pixelData = file.RowOrder == BmpRowOrder.BottomUp
      ? _FlipRows(file.PixelData, stride, file.Height)
      : file.PixelData;

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = paletteCount
    };
  }

  public static BmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    BmpColorMode colorMode;
    int bpp;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (image.Format) {
      case PixelFormat.Bgr24:
        colorMode = BmpColorMode.Rgb24;
        bpp = 24;
        break;
      case PixelFormat.Rgb565:
        colorMode = BmpColorMode.Rgb16_565;
        bpp = 16;
        break;
      case PixelFormat.Indexed8:
        colorMode = BmpColorMode.Palette8;
        bpp = 8;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed4:
        colorMode = BmpColorMode.Palette4;
        bpp = 4;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed1:
        colorMode = BmpColorMode.Palette1;
        bpp = 1;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Gray8:
        colorMode = BmpColorMode.Grayscale8;
        bpp = 8;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for BMP: {image.Format}", nameof(image));
    }

    return new BmpFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = image.PixelData,
      Palette = palette,
      PaletteColorCount = paletteCount,
      RowOrder = BmpRowOrder.TopDown,
      Compression = BmpCompression.None,
      ColorMode = colorMode
    };
  }

  private static byte[] _FlipRows(byte[] data, int stride, int height) {
    var result = new byte[data.Length];
    for (var y = 0; y < height; ++y)
      data.AsSpan((height - 1 - y) * stride, stride).CopyTo(result.AsSpan(y * stride));
    return result;
  }
}
