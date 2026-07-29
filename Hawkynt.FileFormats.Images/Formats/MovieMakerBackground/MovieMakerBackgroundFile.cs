using System;
using FileFormat.Core;

namespace FileFormat.MovieMakerBackground;

/// <summary>In-memory representation of an Atari 8-bit Movie Maker background (.bkg) screen.</summary>
/// <remarks>
/// A fixed 3856-byte file: a 3840-byte ANTIC mode D ("Graphics 7") bitmap, then four GTIA colour
/// bytes in the order the pixel values use them — background first, then PF0, PF1 and PF2 — and
/// finally twelve unused bytes. The 160x96 logical pixels are displayed at 320x192.
/// </remarks>
public readonly record struct MovieMakerBackgroundFile
  : IImageFormatReader<MovieMakerBackgroundFile>, IImageToRawImage<MovieMakerBackgroundFile>,
    IImageFromRawImage<MovieMakerBackgroundFile>, IImageFormatWriter<MovieMakerBackgroundFile> {

  /// <summary>Logical bitmap width.</summary>
  public const int BitmapWidth = Atari8BitGraphics.Gr7Width;

  /// <summary>Number of stored scanlines.</summary>
  public const int BitmapHeight = 96;

  /// <summary>Displayed width; each logical pixel is two screen pixels wide.</summary>
  public const int DisplayWidth = BitmapWidth * 2;

  /// <summary>Displayed height; each stored scanline is shown twice.</summary>
  public const int DisplayHeight = BitmapHeight * 2;

  /// <summary>Size of the bitmap section.</summary>
  public const int BitmapDataSize = Atari8BitGraphics.Gr7BytesPerRow * BitmapHeight;

  /// <summary>Offset of the colour bytes, immediately after the bitmap.</summary>
  public const int ColorOffset = BitmapDataSize;

  /// <summary>Colours a Graphics 7 screen can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Total file size; twelve bytes of padding follow the colours.</summary>
  public const int FileSize = 3856;

  static string IImageFormatMetadata<MovieMakerBackgroundFile>.PrimaryExtension => ".bkg";
  static string[] IImageFormatMetadata<MovieMakerBackgroundFile>.FileExtensions => [".bkg"];
  static MovieMakerBackgroundFile IImageFormatReader<MovieMakerBackgroundFile>.FromSpan(ReadOnlySpan<byte> data)
    => MovieMakerBackgroundReader.FromSpan(data);
  static byte[] IImageFormatWriter<MovieMakerBackgroundFile>.ToBytes(MovieMakerBackgroundFile file)
    => MovieMakerBackgroundWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MovieMakerBackgroundFile>.VideoModes => [
    new("Graphics 7", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed Graphics 7 bitmap.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The four GTIA colour bytes, indexed by pixel value.</summary>
  public byte[] Colors { get; init; }

  public static RawImage ToRawImage(MovieMakerBackgroundFile file) {
    var pixels = Atari8BitGraphics.UnpackGr7(file.BitmapData, 0, BitmapHeight);
    var gtia = Atari8BitGraphics.CreatePalette();

    // The file already stores its colours in pixel-value order, so no register remap is needed.
    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value) {
      var colorByte = value < file.Colors.Length ? file.Colors[value] : (byte)0;
      Array.Copy(gtia, colorByte * 3, palette, value * 3, 3);
    }

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[(y >> 1) * BitmapWidth + (x >> 1)];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static MovieMakerBackgroundFile FromRawImage(RawImage image) {
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

    var pixels = new byte[BitmapWidth * BitmapHeight];
    for (var y = 0; y < BitmapHeight; ++y)
    for (var x = 0; x < BitmapWidth; ++x) {
      var source = y * 2 * DisplayWidth + x * 2;
      var b = indexed.PixelData[source >> 1];
      var index = (source & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F;
      pixels[y * BitmapWidth + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, BitmapHeight),
      Colors = colors,
    };
  }
}
