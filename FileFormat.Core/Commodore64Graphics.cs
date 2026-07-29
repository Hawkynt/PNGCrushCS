using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Commodore 64 picture formats.</summary>
/// <remarks>
/// The machine has no palette to store: all sixteen colours are fixed in the VIC-II, and a file
/// only ever names them by index. Every C64 format therefore needs the same table, which is why it
/// lives here instead of in each of them.
/// </remarks>
public static class Commodore64Graphics {

  /// <summary>Colours the VIC-II can show.</summary>
  public const int ColorCount = 16;

  /// <summary>The fixed sixteen colours as 0xRRGGBB values, in hardware index order.</summary>
  public static ReadOnlySpan<int> HexColors => [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>The palette as RGB triplets, ready for <see cref="RawImage.Palette"/>.</summary>
  public static byte[] CreatePalette() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var color = HexColors[i];
      palette[i * 3] = (byte)(color >> 16);
      palette[i * 3 + 1] = (byte)(color >> 8);
      palette[i * 3 + 2] = (byte)color;
    }

    return palette;
  }

  /// <summary>Character cells across a screen.</summary>
  public const int Columns = 40;

  /// <summary>Pixel rows in one character cell.</summary>
  public const int CellHeight = 8;

  /// <summary>
  /// Decodes a standard multicolour screen: a bitmap of two bits per pixel, a video matrix holding
  /// two colours per cell and a colour RAM holding a third, with pattern 00 taken from the shared
  /// background register.
  /// </summary>
  /// <remarks>
  /// This is the layout Koala Painter established and roughly a dozen other programs copied
  /// verbatim, differing only in what they wrap it in.
  /// </remarks>
  public static RawImage DecodeMulticolor(
    ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> videoMatrix, ReadOnlySpan<byte> colorRam,
    byte background, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 4;
      var bitmapByte = bitmap[cellIndex * CellHeight + y % CellHeight];
      var pattern = (bitmapByte >> ((3 - x % 4) * 2)) & 3;

      var colorIndex = pattern switch {
        0 => background & 0x0F,
        1 => (videoMatrix[cellIndex] >> 4) & 0x0F,
        2 => videoMatrix[cellIndex] & 0x0F,
        _ => colorRam[cellIndex] & 0x0F,
      };

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Decodes a standard high-resolution screen: one bit per pixel choosing between the two colours
  /// the cell's video matrix byte names, foreground in the high nibble.
  /// </summary>
  public static RawImage DecodeHires(ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> screenRam, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 8;
      var bitmapByte = bitmap[cellIndex * CellHeight + y % CellHeight];
      var attribute = screenRam[cellIndex];
      var colorIndex = ((bitmapByte >> (7 - x % 8)) & 1) == 1 ? (attribute >> 4) & 0x0F : attribute & 0x0F;

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Decodes a multicolour FLI screen straight out of an unparsed payload: the bitmap first, then
  /// one video matrix per line within a cell, then a single colour RAM.
  /// </summary>
  /// <remarks>
  /// FLI switches the video matrix every scanline, which is why the bank is chosen by the row
  /// inside the cell rather than being fixed. Short files are common — the trailing banks are
  /// simply absent — so everything past the bitmap is read defensively and a truncated file
  /// degrades to a two-colour silhouette rather than throwing.
  /// </remarks>
  public static RawImage DecodeFliMulticolor(
    ReadOnlySpan<byte> data, int width, int height,
    int minPayloadSize, int bitmapSize, int screenBankCount, int screenBankSize, int totalScreenSize) {
    var rgb = new byte[width * height * 3];
    var hasFullData = data.Length >= minPayloadSize;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 4;
      var rowInCell = y % CellHeight;
      var bitmapOffset = cellIndex * CellHeight + rowInCell;
      var bitmapByte = bitmapOffset < data.Length ? data[bitmapOffset] : (byte)0;
      var pattern = (bitmapByte >> ((3 - x % 4) * 2)) & 3;

      int colorIndex;
      if (hasFullData) {
        var screenOffset = bitmapSize + rowInCell % screenBankCount * screenBankSize + cellIndex;
        var screenByte = screenOffset < data.Length ? data[screenOffset] : (byte)0;
        var colorOffset = bitmapSize + totalScreenSize + cellIndex;
        var colorByte = colorOffset < data.Length ? data[colorOffset] : (byte)0;

        colorIndex = pattern switch {
          0 => 0,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          _ => colorByte & 0x0F,
        };
      } else
        colorIndex = pattern != 0 ? 1 : 0;

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static void _WriteRgb(byte[] rgb, int offset, int colorIndex) {
    var color = HexColors[colorIndex];
    rgb[offset] = (byte)(color >> 16);
    rgb[offset + 1] = (byte)(color >> 8);
    rgb[offset + 2] = (byte)color;
  }

  /// <summary>The index of the colour closest to a given one, by squared distance in RGB.</summary>
  public static int FindNearestColorIndex(byte red, byte green, byte blue) {
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < ColorCount; ++i) {
      var color = HexColors[i];
      int dr = ((color >> 16) & 0xFF) - red, dg = ((color >> 8) & 0xFF) - green, db = (color & 0xFF) - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return best;
  }
}
