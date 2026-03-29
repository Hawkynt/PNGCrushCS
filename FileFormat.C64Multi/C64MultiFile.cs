using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.C64Multi;

/// <summary>In-memory representation of a C64 multiformat art program image.</summary>
public sealed class C64MultiFile : IImageFileFormat<C64MultiFile> {

  static string IImageFileFormat<C64MultiFile>.PrimaryExtension => ".ocp";
  static string[] IImageFileFormat<C64MultiFile>.FileExtensions => [".ocp", ".hires", ".ami"];
  static C64MultiFile IImageFileFormat<C64MultiFile>.FromFile(FileInfo file) => C64MultiReader.FromFile(file);
  static C64MultiFile IImageFileFormat<C64MultiFile>.FromBytes(byte[] data) => C64MultiReader.FromBytes(data);
  static C64MultiFile IImageFileFormat<C64MultiFile>.FromStream(Stream stream) => C64MultiReader.FromStream(stream);
  static byte[] IImageFileFormat<C64MultiFile>.ToBytes(C64MultiFile file) => C64MultiWriter.ToBytes(file);

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
  internal const int MultiPaddingSize = 15;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width in pixels (320 for hires, 160 for multicolor).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, always 200.</summary>
  public int Height { get; init; }

  /// <summary>The file format variant.</summary>
  public C64MultiFormat Format { get; init; }

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM / video matrix (1000 bytes).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Color RAM (1000 bytes, multicolor only; null for hires).</summary>
  public byte[]? ColorData { get; init; }

  /// <summary>Background/border color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this C64 multi-format image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(C64MultiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Format switch {
      C64MultiFormat.ArtStudioHires => _HiresToRawImage(file),
      C64MultiFormat.ArtStudioMulti or C64MultiFormat.AmicaPaint => _MultiToRawImage(file),
      _ => throw new NotSupportedException($"Unsupported C64 multi format: {file.Format}.")
    };
  }

  /// <summary>Creates an Art Studio Hires (320x200, 1bpp) C64 image from a <see cref="RawImage"/>. Each 8x8 cell picks the two most-common C64 colors.</summary>
  public static C64MultiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var srcWidth = bgra.Width;
    var srcHeight = bgra.Height;
    var src = bgra.PixelData;

    const int width = 320;
    const int height = 200;
    const int cellsX = 40; // 320/8
    const int cellsY = 25; // 200/8
    var bitmapData = new byte[BitmapDataSize]; // 8000
    var screenData = new byte[ScreenDataSize]; // 1000

    for (var cy = 0; cy < cellsY; ++cy)
      for (var cx = 0; cx < cellsX; ++cx) {
        // Find the two most common C64 palette colors in this 8x8 cell
        Span<int> freq = stackalloc int[16];
        freq.Clear();

        for (var py = 0; py < 8; ++py)
          for (var px = 0; px < 8; ++px) {
            var x = cx * 8 + px;
            var y = cy * 8 + py;
            byte r = 0, g = 0, b = 0;
            if (x < srcWidth && y < srcHeight) {
              var si = (y * srcWidth + x) * 4;
              b = src[si]; g = src[si + 1]; r = src[si + 2];
            }

            ++freq[_FindNearestC64Color(r, g, b)];
          }

        // Pick top 2 colors
        var fg = 0;
        var bg = 0;
        var maxFreq = -1;
        var secondFreq = -1;
        for (var i = 0; i < 16; ++i)
          if (freq[i] > maxFreq) {
            secondFreq = maxFreq;
            bg = fg;
            maxFreq = freq[i];
            fg = i;
          } else if (freq[i] > secondFreq) {
            secondFreq = freq[i];
            bg = i;
          }

        var cellIndex = cy * cellsX + cx;
        screenData[cellIndex] = (byte)((fg << 4) | (bg & 0x0F));

        // Encode bitmap: 1 = fg, 0 = bg
        for (var py = 0; py < 8; ++py) {
          byte bitmapByte = 0;
          for (var px = 0; px < 8; ++px) {
            var x = cx * 8 + px;
            var y = cy * 8 + py;
            byte r = 0, g = 0, b2 = 0;
            if (x < srcWidth && y < srcHeight) {
              var si = (y * srcWidth + x) * 4;
              b2 = src[si]; g = src[si + 1]; r = src[si + 2];
            }

            var nearest = _FindNearestC64Color(r, g, b2);
            if (nearest == fg)
              bitmapByte |= (byte)(1 << (7 - px));
          }

          bitmapData[cellIndex * 8 + py] = bitmapByte;
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      BackgroundColor = 0,
    };
  }

  private static int _FindNearestC64Color(byte r, byte g, byte b) {
    var bestDist = int.MaxValue;
    var bestIdx = 0;
    for (var i = 0; i < 16; ++i) {
      var c = _C64Palette[i];
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

        var color = _C64Palette[colorIndex];
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

        var color = _C64Palette[colorIndex];
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
