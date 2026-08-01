using System;
using FileFormat.Core;

namespace FileFormat.C64Multi;

/// <summary>In-memory representation of a C64 multiformat art program image.</summary>
public readonly record struct C64MultiFile : IImageFormatReader<C64MultiFile>, IImageToRawImage<C64MultiFile>, IImageFromRawImage<C64MultiFile>, IImageFormatWriter<C64MultiFile> {

  static string IImageFormatMetadata<C64MultiFile>.PrimaryExtension => ".ocp";
  static string[] IImageFormatMetadata<C64MultiFile>.FileExtensions => [".ocp", ".hires", ".ami"];
  static C64MultiFile IImageFormatReader<C64MultiFile>.FromSpan(ReadOnlySpan<byte> data) => C64MultiReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<C64MultiFile>.VideoModes => [
    new("Default", [(320, 200)])
  ];
  static byte[] IImageFormatWriter<C64MultiFile>.ToBytes(C64MultiFile file) => C64MultiWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes (multicolor only).</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Expected file size for Art Studio Hires: 2 + 8000 + 1000 + 1 + 6 = 9009.</summary>
  public const int ArtStudioHiresFileSize = 9009;

  /// <summary>Expected file size for Art Studio Multicolor: 2 + 8000 + 1000 + 1000 + 1 + 15 = 10018.</summary>
  public const int ArtStudioMultiFileSize = 10018;

  /// <summary>Hires padding after border color byte.</summary>
  internal const int HiresPaddingSize = 6;

  /// <summary>Multicolor padding after background color byte.</summary>
  /// <summary>Where the background colour sits, a byte after the screen rather than at the end.</summary>
  /// <remarks>
  /// The order is bitmap, screen, then sixteen bytes of which the second is the background, and only
  /// then the colour RAM. Writing the colour RAM straight after the screen puts it sixteen bytes
  /// early and leaves the background at the far end of the file, which is a picture in the right
  /// shape drawn in whatever the last kilobyte happened to be.
  /// </remarks>
  internal const int MultiBackgroundOffset = 9003;

  /// <summary>Where the colour RAM starts.</summary>
  internal const int MultiColorOffset = 9018;

  internal const int MultiPaddingSize = 15;

  /// <summary>Image width in pixels (320 for hires, 160 for multicolor).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, always 200.</summary>
  public int Height { get; init; }

  /// <summary>The file format variant.</summary>
  public C64MultiFormat Format { get; init; }

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM / video matrix (1000 bytes).</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Color RAM (1000 bytes, multicolor only; null for hires).</summary>
  public byte[]? ColorData { get; init; }

  /// <summary>Background/border color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this C64 multi-format image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(C64MultiFile file) {

    return file.Format switch {
      C64MultiFormat.ArtStudioHires => _HiresToRawImage(file),
      C64MultiFormat.ArtStudioMulti or C64MultiFormat.AmicaPaint => _MultiToRawImage(file),
      _ => throw new NotSupportedException($"Unsupported C64 multi format: {file.Format}.")
    };
  }

  /// <summary>Creates an Art Studio Hires (320x200, 1bpp) C64 image from a <see cref="RawImage"/>. Each 8x8 cell picks the two most-common C64 colors.</summary>
  /// <summary>Builds a multicolour screen, which is what the extension this is written under means.</summary>
  /// <remarks>
  /// This used to produce the high-resolution variant whatever it was asked for — a different length
  /// and a different layout from the one .ocp names, so nothing that knew the extension would open
  /// it. Multicolour is also the more capable of the two: three colours a cell against two, bought
  /// with half the horizontal resolution.
  /// </remarks>
  public static C64MultiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = 160;
    const int height = 200;

    var rgb = image.SampleTo(width, height);
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    var colors = new byte[ColorDataSize];
    var background = Commodore64Graphics.EncodeMulticolor(rgb.PixelData, width, height, bitmap, screen, colors);

    return new() {
      Width = width,
      Height = height,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = colors,
      BackgroundColor = background,
    };
  }

  private static int _FindNearestC64Color(byte r, byte g, byte b) {
    var bestDist = int.MaxValue;
    var bestIdx = 0;
    for (var i = 0; i < 16; ++i) {
      var c = Commodore64Graphics.HexColors[i];
      var cr = (c >> 16) & 0xFF;
      var cg = (c >> 8) & 0xFF;
      var cb = c & 0xFF;
      var dr = r - cr;
      var dg = g - cg;
      var db = b - cb;
      var dist = dr * dr + dg * dg + db * db;
      if (dist < bestDist) {
        bestDist = dist;
        bestIdx = i;
      }
    }

    return bestIdx;
  }

  private static RawImage _HiresToRawImage(C64MultiFile file) {
    const int width = 320;
    const int height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = file.ScreenData[cellIndex];
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

        var color = Commodore64Graphics.HexColors[colorIndex];
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

  private static RawImage _MultiToRawImage(C64MultiFile file) {
    const int width = 160;
    const int height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenData[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenData[cellIndex] & 0x0F,
          3 => (file.ColorData?[cellIndex] ?? 0) & 0x0F,
          _ => 0
        };

        var color = Commodore64Graphics.HexColors[colorIndex];
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
}
