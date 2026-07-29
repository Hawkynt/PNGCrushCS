using System;
using FileFormat.Core;

namespace FileFormat.ZxMulticolor;

/// <summary>In-memory representation of a ZX Spectrum Multicolor file (12288 bytes: 6144 bitmap + 6144 per-scanline attributes).</summary>
public readonly record struct ZxMulticolorFile : IImageFormatReader<ZxMulticolorFile>, IImageToRawImage<ZxMulticolorFile>, IImageFromRawImage<ZxMulticolorFile>, IImageFormatWriter<ZxMulticolorFile> {

  static string IImageFormatMetadata<ZxMulticolorFile>.PrimaryExtension => ".mlt";
  static string[] IImageFormatMetadata<ZxMulticolorFile>.FileExtensions => [".mlt", ".mc"];
  static ZxMulticolorFile IImageFormatReader<ZxMulticolorFile>.FromSpan(ReadOnlySpan<byte> data) => ZxMulticolorReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxMulticolorFile>.ToBytes(ZxMulticolorFile file) => ZxMulticolorWriter.ToBytes(file);

  /// <summary>ZX Spectrum normal palette (bright=0).</summary>
  internal static readonly int[] NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  internal static readonly int[] BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap in the Spectrum display-file order.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>6144 bytes of per-scanline attribute data (32 attributes per scanline, 192 scanlines).</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>Converts this multicolor screen to Rgb24.</summary>
  public static RawImage ToRawImage(ZxMulticolorFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = ZxSpectrumGraphics.LineOffset(y) + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var attribute = file.AttributeData[y * 32 + cellX];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? BrightPalette : NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }


  /// <summary>Builds a multicolor screen from an arbitrary image.</summary>
  /// <remarks>
  /// The Spectrum allows two colours per attribute cell, and in this format a cell is 8x1 — so
  /// every scanline gets its own row of attributes. Each cell is reduced to the two palette
  /// entries that appear most in it, and the bitmap then records which of the two each pixel took.
  /// </remarks>
  public static ZxMulticolorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ZxSpectrumGraphics.ScreenWidth || image.Height != ZxSpectrumGraphics.ScreenHeight)
      throw new ArgumentException(
        $"Expected {ZxSpectrumGraphics.ScreenWidth}x{ZxSpectrumGraphics.ScreenHeight} but got {image.Width}x{image.Height}.",
        nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    var bitmap = new byte[ZxSpectrumGraphics.BitmapSize];
    var attributes = new byte[ZxSpectrumGraphics.BitmapSize];

    Span<int> counts = stackalloc int[ZxSpectrumGraphics.PaletteEntryCount];
    for (var y = 0; y < ZxSpectrumGraphics.ScreenHeight; ++y)
    for (var cell = 0; cell < ZxSpectrumGraphics.BytesPerRow; ++cell) {
      counts.Clear();
      for (var i = 0; i < 8; ++i)
        ++counts[indexed.PixelData[y * ZxSpectrumGraphics.ScreenWidth + cell * 8 + i] & 15];

      // Most common colour becomes paper, runner-up becomes ink.
      int paper = 0, ink = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink == paper ? paper : ink])
          ink = c;

      attributes[y * ZxSpectrumGraphics.BytesPerRow + cell] = ZxSpectrumGraphics.Attribute(ink, paper);

      var bits = 0;
      for (var i = 0; i < 8; ++i)
        if ((indexed.PixelData[y * ZxSpectrumGraphics.ScreenWidth + cell * 8 + i] & 15) == ink)
          bits |= 0x80 >> i;

      bitmap[ZxSpectrumGraphics.LineOffset(y) + cell] = (byte)bits;
    }

    return new() { BitmapData = bitmap, AttributeData = attributes };
  }
}
