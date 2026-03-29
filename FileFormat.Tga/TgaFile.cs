using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Tga;

/// <summary>In-memory representation of a TGA image.</summary>
public sealed class TgaFile : IImageFileFormat<TgaFile> {

  static string IImageFileFormat<TgaFile>.PrimaryExtension => ".tga";
  static string[] IImageFileFormat<TgaFile>.FileExtensions => [".tga", ".vda", ".icb", ".vst", ".bpx", ".targa", ".ivb"];
  static FormatCapability IImageFileFormat<TgaFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static TgaFile IImageFileFormat<TgaFile>.FromFile(FileInfo file) => TgaReader.FromFile(file);
  static TgaFile IImageFileFormat<TgaFile>.FromBytes(byte[] data) => TgaReader.FromBytes(data);
  static TgaFile IImageFileFormat<TgaFile>.FromStream(Stream stream) => TgaReader.FromStream(stream);
  static byte[] IImageFileFormat<TgaFile>.ToBytes(TgaFile file) => TgaWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public TgaOrigin Origin { get; init; }
  public TgaCompression Compression { get; init; }
  public TgaColorMode ColorMode { get; init; }

  public static RawImage ToRawImage(TgaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var mode = file.ColorMode;
    if (mode == TgaColorMode.Original)
      mode = file.BitsPerPixel switch {
        32 => TgaColorMode.Rgba32,
        24 => TgaColorMode.Rgb24,
        8 when file.Palette != null => TgaColorMode.Indexed8,
        _ => TgaColorMode.Grayscale8
      };

    PixelFormat format;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (mode) {
      case TgaColorMode.Rgba32:
        format = PixelFormat.Bgra32;
        break;
      case TgaColorMode.Rgb24:
        format = PixelFormat.Bgr24;
        break;
      case TgaColorMode.Grayscale8:
        format = PixelFormat.Gray8;
        break;
      case TgaColorMode.Indexed8:
        format = PixelFormat.Indexed8;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      default:
        throw new ArgumentException($"Unsupported TgaColorMode: {mode}", nameof(file));
    }

    var bpp = file.BitsPerPixel;
    var stride = bpp >= 8 ? file.Width * (bpp / 8) : (file.Width + 7) / 8;
    var pixelData = file.Origin == TgaOrigin.BottomLeft
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

  public static TgaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    TgaColorMode colorMode;
    int bpp;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (image.Format) {
      case PixelFormat.Bgra32:
        colorMode = TgaColorMode.Rgba32;
        bpp = 32;
        break;
      case PixelFormat.Bgr24:
        colorMode = TgaColorMode.Rgb24;
        bpp = 24;
        break;
      case PixelFormat.Gray8:
        colorMode = TgaColorMode.Grayscale8;
        bpp = 8;
        break;
      case PixelFormat.Indexed8:
        colorMode = TgaColorMode.Indexed8;
        bpp = 8;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for TGA: {image.Format}", nameof(image));
    }

    return new TgaFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = image.PixelData,
      Palette = palette,
      PaletteColorCount = paletteCount,
      Origin = TgaOrigin.TopLeft,
      Compression = TgaCompression.None,
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
