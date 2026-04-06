using System;
using FileFormat.Core;

namespace FileFormat.ExtendedGemImg;

/// <summary>In-memory representation of an Extended GEM Bit Image (XIMG) raster image.</summary>
public readonly record struct ExtendedGemImgFile : IImageFormatReader<ExtendedGemImgFile>, IImageToRawImage<ExtendedGemImgFile>, IImageFromRawImage<ExtendedGemImgFile>, IImageFormatWriter<ExtendedGemImgFile> {

  static string IImageFormatMetadata<ExtendedGemImgFile>.PrimaryExtension => ".ximg";
  static string[] IImageFormatMetadata<ExtendedGemImgFile>.FileExtensions => [".ximg"];
  static ExtendedGemImgFile IImageFormatReader<ExtendedGemImgFile>.FromSpan(ReadOnlySpan<byte> data) => ExtendedGemImgReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<ExtendedGemImgFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<ExtendedGemImgFile>.ToBytes(ExtendedGemImgFile file) => ExtendedGemImgWriter.ToBytes(file);

  /// <summary>IMG version (typically 1).</summary>
  public int Version { get; init; }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in scan lines.</summary>
  public int Height { get; init; }

  /// <summary>Number of color planes.</summary>
  public int NumPlanes { get; init; }

  /// <summary>Pattern replication length in bytes.</summary>
  public int PatternLength { get; init; }

  /// <summary>Pixel width for aspect ratio (e.g. 85).</summary>
  public int PixelWidth { get; init; }

  /// <summary>Pixel height for aspect ratio (e.g. 85).</summary>
  public int PixelHeight { get; init; }

  /// <summary>Color model used in the palette (RGB, CMY, Pantone).</summary>
  public ExtendedGemImgColorModel ColorModel { get; init; }

  /// <summary>
  ///   Palette data: each entry is 3 shorts (R, G, B or C, M, Y) in the range 0-1000.
  ///   Array length = paletteCount * 3.
  /// </summary>
  public short[] PaletteData { get; init; }

  /// <summary>Non-interleaved planar pixel data (plane-by-plane, row-by-row).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this XIMG file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(ExtendedGemImgFile file) {

    var chunky = PlanarConverter.NonInterleavedPlanarToChunky(file.PixelData, file.Width, file.Height, file.NumPlanes);
    var paletteCount = Math.Min(1 << file.NumPlanes, 256);

    byte[] rgb;
    if (file.PaletteData.Length >= paletteCount * 3)
      rgb = _ConvertXimgPaletteToRgb(file.PaletteData, paletteCount);
    else
      rgb = _BuildDefaultPalette(paletteCount);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an <see cref="ExtendedGemImgFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static ExtendedGemImgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    var numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(image.PaletteCount, 2))));
    var planar = PlanarConverter.ChunkyToNonInterleavedPlanar(image.PixelData, image.Width, image.Height, numPlanes);
    var paletteData = image.Palette is not null
      ? _ConvertRgbToXimgPalette(image.Palette, image.PaletteCount)
      : [];

    return new() {
      Version = 1,
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      PatternLength = 2,
      PixelWidth = 1,
      PixelHeight = 1,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = paletteData,
      PixelData = planar,
    };
  }

  /// <summary>Converts XIMG palette (0-1000 range per component) to RGB24 (0-255 range).</summary>
  private static byte[] _ConvertXimgPaletteToRgb(short[] paletteData, int count) {
    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var srcIdx = i * 3;
      var r = Math.Clamp(paletteData[srcIdx] * 255 / 1000, 0, 255);
      var g = Math.Clamp(paletteData[srcIdx + 1] * 255 / 1000, 0, 255);
      var b = Math.Clamp(paletteData[srcIdx + 2] * 255 / 1000, 0, 255);
      rgb[i * 3] = (byte)r;
      rgb[i * 3 + 1] = (byte)g;
      rgb[i * 3 + 2] = (byte)b;
    }
    return rgb;
  }

  /// <summary>Converts RGB24 palette (0-255) to XIMG palette (0-1000).</summary>
  private static short[] _ConvertRgbToXimgPalette(byte[] rgb, int count) {
    var paletteData = new short[count * 3];
    for (var i = 0; i < count; ++i) {
      var srcIdx = i * 3;
      paletteData[i * 3] = (short)(rgb[srcIdx] * 1000 / 255);
      paletteData[i * 3 + 1] = (short)(rgb[srcIdx + 1] * 1000 / 255);
      paletteData[i * 3 + 2] = (short)(rgb[srcIdx + 2] * 1000 / 255);
    }
    return paletteData;
  }

  /// <summary>Builds a default GEM-style palette (index 0 = white, index 1 = black, rest evenly spaced).</summary>
  private static byte[] _BuildDefaultPalette(int count) {
    var palette = new byte[count * 3];
    palette[0] = 255;
    palette[1] = 255;
    palette[2] = 255;
    if (count > 1) {
      palette[3] = 0;
      palette[4] = 0;
      palette[5] = 0;
    }
    for (var i = 2; i < count; ++i) {
      var gray = (byte)(255 - i * 255 / (count - 1));
      palette[i * 3] = gray;
      palette[i * 3 + 1] = gray;
      palette[i * 3 + 2] = gray;
    }
    return palette;
  }
}
