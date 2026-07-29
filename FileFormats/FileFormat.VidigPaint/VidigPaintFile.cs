using System;
using FileFormat.Core;

namespace FileFormat.VidigPaint;

/// <summary>In-memory representation of Atari 8-bit Vidig Paint (.rap) screens.</summary>
/// <remarks>
/// A fixed 7680-byte file: 0 header bytes followed by an ANTIC mode F
/// ("Graphics 9") screen of 192 rows. Mode 9 gives 16 luminance levels of a single hue; each
/// stored nibble covers four screen pixels and each stored row four screen rows, so the picture is
/// displayed at 320x192.
/// </remarks>
public readonly record struct VidigPaintFile
  : IImageFormatReader<VidigPaintFile>, IImageToRawImage<VidigPaintFile>,
    IImageFromRawImage<VidigPaintFile>, IImageFormatWriter<VidigPaintFile> {

  /// <summary>Header bytes preceding the screen.</summary>
  public const int HeaderSize = 0;

  /// <summary>Screen width in stored pixels.</summary>
  public const int ScreenWidth = 320;

  /// <summary>Stored row count.</summary>
  public const int ScreenRows = 192;

  /// <summary>Bytes per stored row.</summary>
  public const int BytesPerRow = ScreenWidth / 8;

  /// <summary>Size of the screen section.</summary>
  public const int ScreenDataSize = BytesPerRow * ScreenRows;

  /// <summary>Offset of the trailing background-colour byte.</summary>
  public const int BackgroundColorOffset = HeaderSize + ScreenDataSize;

  /// <summary>Total file size: the screen plus one background-colour byte.</summary>
  public const int FileSize = BackgroundColorOffset + 1;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 192;

  /// <summary>Luminance levels a Graphics 9 screen can show.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<VidigPaintFile>.PrimaryExtension => ".rap";
  static string[] IImageFormatMetadata<VidigPaintFile>.FileExtensions => [".rap"];
  static VidigPaintFile IImageFormatReader<VidigPaintFile>.FromSpan(ReadOnlySpan<byte> data) => VidigPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<VidigPaintFile>.ToBytes(VidigPaintFile file) => VidigPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<VidigPaintFile>.VideoModes => [
    new("Graphics 9", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Header bytes, preserved verbatim.</summary>
  public byte[] Header { get; init; }

  /// <summary>Packed Graphics 9 screen.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>GTIA background colour byte. Mode F contributes luminance only, so this sets the hue
  /// the whole screen is rendered in.</summary>
  public byte BackgroundColor { get; init; }

  public static RawImage ToRawImage(VidigPaintFile file) {
    var pixels = Atari8BitGraphics.UnpackGr9(file.ScreenData, 0, ScreenWidth, ScreenRows);

    // Unlike most mode F formats this one names its hue, so the luminance ramp is built by
    // combining the stored background colour with each level.
    var gtia = Atari8BitGraphics.CreatePalette();
    var hue = file.BackgroundColor & 0xF0;
    var palette = new byte[ColorCount * 3];
    for (var level = 0; level < ColorCount; ++level)
      Array.Copy(gtia, (hue | level) * 3, palette, level * 3, 3);

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[y * ScreenRows / DisplayHeight * ScreenWidth + x * ScreenWidth / DisplayWidth];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static VidigPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Luminance only: take the grey level of each sampled pixel.
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var pixels = new byte[ScreenWidth * ScreenRows];
    for (var y = 0; y < ScreenRows; ++y)
    for (var x = 0; x < ScreenWidth; ++x) {
      var sourceX = x * DisplayWidth / ScreenWidth;
      var sourceY = y * DisplayHeight / ScreenRows;
      pixels[y * ScreenWidth + x] = (byte)(grey.PixelData[sourceY * DisplayWidth + sourceX] * (ColorCount - 1) / 255);
    }

    return new() {
      Header = new byte[HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, ScreenWidth, ScreenRows),
      BackgroundColor = 0,
    };
  }
}
