using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Tiff;

/// <summary>In-memory representation of a TIFF image.</summary>
public sealed class TiffFile : IImageFormatReader<TiffFile>, IImageToRawImage<TiffFile>, IImageFromRawImage<TiffFile>, IImageFormatWriter<TiffFile>, IMultiImageFileFormat<TiffFile> {

  static string IImageFormatMetadata<TiffFile>.PrimaryExtension => ".tiff";
  static string[] IImageFormatMetadata<TiffFile>.FileExtensions => [".tif", ".tiff", ".ftf"];
  static TiffFile IImageFormatReader<TiffFile>.FromSpan(ReadOnlySpan<byte> data) => TiffReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<TiffFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;

  static bool? IImageFormatMetadata<TiffFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
      return true;
    if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
      return true;
    return null;
  }

  static byte[] IImageFormatWriter<TiffFile>.ToBytes(TiffFile file) => TiffWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; }
  public int BitsPerSample { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? ColorMap { get; init; }
  public TiffColorMode ColorMode { get; init; }

  /// <summary>Additional pages beyond the first IFD. Empty for single-page TIFFs.</summary>
  public IReadOnlyList<TiffPage> Pages { get; init; } = [];

  /// <summary>Returns the total number of pages (IFDs) in the TIFF file.</summary>
  public static int ImageCount(TiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return 1 + file.Pages.Count;
  }

  /// <summary>Converts a specific page at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(TiffFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    var total = 1 + file.Pages.Count;
    if ((uint)index >= (uint)total)
      throw new ArgumentOutOfRangeException(nameof(index));

    if (index == 0)
      return ToRawImage(file);

    var page = file.Pages[index - 1];
    return _PageToRawImage(page);
  }

  private static RawImage _PageToRawImage(TiffPage page) {
    PixelFormat format;
    byte[]? palette = null;
    var paletteCount = 0;

    switch (page.SamplesPerPixel) {
      case 3 when page.BitsPerSample == 8:
        format = PixelFormat.Rgb24;
        break;
      case 4 when page.BitsPerSample == 8:
        format = PixelFormat.Rgba32;
        break;
      case 1 when page.BitsPerSample == 8 && page.ColorMap != null:
        format = PixelFormat.Indexed8;
        palette = _ConvertTiffColorMap(page.ColorMap);
        paletteCount = page.ColorMap.Length / 6;
        break;
      case 1 when page.BitsPerSample == 8:
        format = PixelFormat.Gray8;
        break;
      case 1 when page.BitsPerSample == 16:
        format = PixelFormat.Gray16;
        break;
      case 1 when page.BitsPerSample == 1:
        format = PixelFormat.Indexed1;
        palette = [0, 0, 0, 255, 255, 255];
        paletteCount = 2;
        break;
      default:
        throw new ArgumentException($"Unsupported TIFF page configuration: SamplesPerPixel={page.SamplesPerPixel}, BitsPerSample={page.BitsPerSample}.");
    }

    return new() {
      Width = page.Width,
      Height = page.Height,
      Format = format,
      PixelData = page.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  public static RawImage ToRawImage(TiffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    PixelFormat format;
    byte[]? palette = null;
    var paletteCount = 0;

    switch (file.SamplesPerPixel) {
      case 3 when file.BitsPerSample == 8:
        format = PixelFormat.Rgb24;
        break;
      case 4 when file.BitsPerSample == 8:
        format = PixelFormat.Rgba32;
        break;
      case 1 when file.BitsPerSample == 8 && file.ColorMap != null:
        format = PixelFormat.Indexed8;
        palette = _ConvertTiffColorMap(file.ColorMap);
        paletteCount = file.ColorMap.Length / 6;
        break;
      case 1 when file.BitsPerSample == 8:
        format = PixelFormat.Gray8;
        break;
      case 1 when file.BitsPerSample == 16:
        format = PixelFormat.Gray16;
        break;
      case 1 when file.BitsPerSample == 1:
        format = PixelFormat.Indexed1;
        palette = [0, 0, 0, 255, 255, 255];
        paletteCount = 2;
        break;
      default:
        throw new ArgumentException($"Unsupported TIFF configuration: SamplesPerPixel={file.SamplesPerPixel}, BitsPerSample={file.BitsPerSample}.", nameof(file));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  public static TiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int samplesPerPixel;
    int bitsPerSample;
    TiffColorMode colorMode;
    byte[]? colorMap = null;

    switch (image.Format) {
      case PixelFormat.Rgb24:
        samplesPerPixel = 3;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Rgb;
        break;
      case PixelFormat.Rgba32:
        samplesPerPixel = 4;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Rgb;
        break;
      case PixelFormat.Gray8:
        samplesPerPixel = 1;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Grayscale;
        break;
      case PixelFormat.Gray16:
        samplesPerPixel = 1;
        bitsPerSample = 16;
        colorMode = TiffColorMode.Grayscale;
        break;
      case PixelFormat.Indexed8:
        samplesPerPixel = 1;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Palette;
        colorMap = _ConvertToTiffColorMap(image.Palette, image.PaletteCount);
        break;
      case PixelFormat.Indexed1:
        samplesPerPixel = 1;
        bitsPerSample = 1;
        colorMode = TiffColorMode.BiLevel;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for TIFF: {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = image.PixelData[..],
      ColorMap = colorMap,
      ColorMode = colorMode,
    };
  }

  /// <summary>Converts TIFF ColorMap (16-bit interleaved R,G,B arrays) to RGB triplet palette.</summary>
  private static byte[] _ConvertTiffColorMap(byte[] colorMap) {
    var entryCount = colorMap.Length / 6;
    var palette = new byte[entryCount * 3];
    for (var i = 0; i < entryCount; ++i) {
      // TIFF ColorMap: all reds (16-bit LE), then all greens, then all blues
      palette[i * 3] = colorMap[i * 2 + 1];
      palette[i * 3 + 1] = colorMap[entryCount * 2 + i * 2 + 1];
      palette[i * 3 + 2] = colorMap[entryCount * 4 + i * 2 + 1];
    }

    return palette;
  }

  /// <summary>Converts RGB triplet palette to TIFF ColorMap format (16-bit interleaved R,G,B arrays).</summary>
  private static byte[] _ConvertToTiffColorMap(byte[]? palette, int paletteCount) {
    if (palette == null)
      throw new ArgumentException("Palette must not be null for indexed images.");

    var colorMap = new byte[paletteCount * 6];
    for (var i = 0; i < paletteCount; ++i) {
      // TIFF ColorMap: all reds (16-bit LE), then all greens, then all blues
      colorMap[i * 2] = 0;
      colorMap[i * 2 + 1] = palette[i * 3];
      colorMap[paletteCount * 2 + i * 2] = 0;
      colorMap[paletteCount * 2 + i * 2 + 1] = palette[i * 3 + 1];
      colorMap[paletteCount * 4 + i * 2] = 0;
      colorMap[paletteCount * 4 + i * 2 + 1] = palette[i * 3 + 2];
    }

    return colorMap;
  }
}
