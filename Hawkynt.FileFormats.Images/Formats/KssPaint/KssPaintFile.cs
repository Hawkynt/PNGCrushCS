using System;
using FileFormat.Core;

namespace FileFormat.KssPaint;

/// <summary>In-memory representation of an Atari 8-bit KSS-Paint (.kss) screen.</summary>
/// <remarks>
/// A fixed 6404-byte file: a 6400-byte ANTIC mode E ("Graphics 15") bitmap followed by four GTIA
/// colour bytes in background-first order. Mode E uses the same two-bits-per-pixel packing as
/// mode D, but draws one screen row per stored row instead of two, so 160 stored rows fill a
/// 320x160 display.
/// </remarks>
public readonly record struct KssPaintFile
  : IImageFormatReader<KssPaintFile>, IImageToRawImage<KssPaintFile>,
    IImageFromRawImage<KssPaintFile>, IImageFormatWriter<KssPaintFile> {

  /// <summary>Stored scanlines.</summary>
  public const int BitmapHeight = 160;

  /// <summary>Displayed width; each logical pixel is two screen pixels wide.</summary>
  public const int DisplayWidth = Atari8BitGraphics.Gr7Width * 2;

  /// <summary>Displayed height; mode E does not double rows.</summary>
  public const int DisplayHeight = BitmapHeight;

  /// <summary>Size of the bitmap section.</summary>
  public const int BitmapDataSize = Atari8BitGraphics.Gr7BytesPerRow * BitmapHeight;

  /// <summary>Offset of the colour bytes.</summary>
  public const int ColorOffset = BitmapDataSize;

  /// <summary>Colour bytes stored: background, then PF0, PF1 and PF2.</summary>
  public const int ColorCount = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorOffset + ColorCount;

  static string IImageFormatMetadata<KssPaintFile>.PrimaryExtension => ".kss";
  static string[] IImageFormatMetadata<KssPaintFile>.FileExtensions => [".kss"];
  static KssPaintFile IImageFormatReader<KssPaintFile>.FromSpan(ReadOnlySpan<byte> data) => KssPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<KssPaintFile>.ToBytes(KssPaintFile file) => KssPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<KssPaintFile>.VideoModes => [
    new("Graphics 15", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed mode E bitmap.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The four GTIA colour bytes, indexed by pixel value.</summary>
  public byte[] Colors { get; init; }

  public static RawImage ToRawImage(KssPaintFile file) {
    var pixels = Atari8BitGraphics.UnpackGr7(file.BitmapData, 0, BitmapHeight);
    var gtia = Atari8BitGraphics.CreatePalette();

    // Stored background-first, which is the order the pixel values already use.
    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value) {
      var colorByte = value < file.Colors.Length ? file.Colors[value] : (byte)0;
      Array.Copy(gtia, colorByte * 3, palette, value * 3, 3);
    }

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[y * Atari8BitGraphics.Gr7Width + (x >> 1)];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static KssPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[ColorCount];
    for (var value = 0; value < ColorCount && value < indexed.PaletteCount; ++value)
      colors[value] = Atari8BitGraphics.FindNearestColorByte(
        gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);

    var pixels = new byte[Atari8BitGraphics.Gr7Width * BitmapHeight];
    for (var y = 0; y < BitmapHeight; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr7Width; ++x) {
      var source = y * DisplayWidth + x * 2;
      var b = indexed.PixelData[source >> 1];
      var index = (source & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F;
      pixels[y * Atari8BitGraphics.Gr7Width + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, BitmapHeight),
      Colors = colors,
    };
  }
}
