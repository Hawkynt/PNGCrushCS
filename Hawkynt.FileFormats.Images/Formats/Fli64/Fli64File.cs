using System;
using FileFormat.Core;

namespace FileFormat.Fli64;

/// <summary>In-memory representation of a FLI Designer (FLI multicolor) image for the Commodore 64.</summary>
public readonly record struct Fli64File
  : IImageFormatReader<Fli64File>, IImageToRawImage<Fli64File>,
    IImageFromRawImage<Fli64File>, IImageFormatWriter<Fli64File> {

  static string IImageFormatMetadata<Fli64File>.PrimaryExtension => ".fli64";
  static string[] IImageFormatMetadata<Fli64File>.FileExtensions => [".fli64"];
  static Fli64File IImageFormatReader<Fli64File>.FromSpan(ReadOnlySpan<byte> data) => Fli64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Fli64File>.ToBytes(Fli64File file) => Fli64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Fli64File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int FixedWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int FixedHeight = 200;

  /// <summary>Expected file size: 2 + 8000 + 8000 + 1000 + 472 = 17474 bytes.</summary>
  public const int ExpectedFileSize = 17474;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the per-scanline screen data section in bytes (40 bytes x 200 lines).</summary>
  internal const int ScreenDataSize = 8000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 472;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Per-scanline screen RAM (8000 bytes: 40 bytes per scanline x 200 lines).</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Color RAM (1000 bytes, one per 4x8 cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Padding bytes at the end of the file (472 bytes).</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this FLI multicolor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Fli64File file) {
    return _FliMultiToRawImage(file.BitmapData, file.ScreenData, file.ColorRam);
  }

  /// <summary>Shared FLI multicolor decode: per-scanline screen RAM instead of per-cell.</summary>
  internal static RawImage _FliMultiToRawImage(byte[] bitmapData, byte[] screenData, byte[] colorRam) {
    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    // Background color derived from first screen byte
    var backgroundColor = screenData.Length > 0 ? screenData[0] & 0x0F : 0;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = bitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        // FLI uses per-scanline screen RAM: screenData[y * 40 + cellX]
        var screenIndex = y * 40 + cellX;
        var screenByte = screenIndex < screenData.Length ? screenData[screenIndex] : (byte)0;

        var colorIndex = bitValue switch {
          0 => backgroundColor,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          3 => cellIndex < colorRam.Length ? colorRam[cellIndex] & 0x0F : 0,
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

  /// <summary>Builds a FLI multicolor image from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// VIC-II's fixed sixteen colours. Pattern 00 is one colour for the whole picture (the most common
  /// colour overall, stored in the low nibble of the very first screen byte — the one field the format
  /// keeps for it); pattern 11 is one more colour per 4x8 cell (colour RAM); patterns 01 and 10 are free
  /// per 4x1 strip, since FLI swaps its screen RAM bank every scanline. The one exception is the strip at
  /// row 0, column 0: its low nibble is the very byte the background colour lives in, so it can't also
  /// hold a strip-local colour.</summary>
  public static Fli64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"FLI multicolor images are always {FixedWidth}x{FixedHeight}, but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    const int cellsAcross = 40, cellsDown = 25;
    var colorAt = new int[FixedHeight, FixedWidth];

    Span<int> globalFreq = stackalloc int[16];
    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      var o = (y * FixedWidth + x) * 4;
      var color = Commodore64Graphics.FindNearestColorIndex(bgra.PixelData[o + 2], bgra.PixelData[o + 1], bgra.PixelData[o]);
      colorAt[y, x] = color;
      ++globalFreq[color];
    }

    var background = 0;
    for (var c = 1; c < 16; ++c)
      if (globalFreq[c] > globalFreq[background])
        background = c;

    var bitmap = new byte[BitmapDataSize];
    var screenData = new byte[ScreenDataSize];
    var colorRam = new byte[ColorRamSize];

    Span<int> cellFreq = stackalloc int[16];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      var cellIndex = cellY * cellsAcross + cellX;
      cellFreq.Clear();
      for (var py = 0; py < 8; ++py)
      for (var px = 0; px < 4; ++px) {
        var color = colorAt[cellY * 8 + py, cellX * 4 + px];
        if (color != background)
          ++cellFreq[color];
      }

      var color3 = 0;
      for (var c = 1; c < 16; ++c)
        if (cellFreq[c] > cellFreq[color3])
          color3 = c;

      colorRam[cellIndex] = (byte)color3;

      for (var py = 0; py < 8; ++py) {
        var y = cellY * 8 + py;

        Span<int> rowFreq = stackalloc int[16];
        for (var px = 0; px < 4; ++px) {
          var color = colorAt[y, cellX * 4 + px];
          if (color != background && color != color3)
            ++rowFreq[color];
        }

        int ink1 = 0, ink2 = 0, best1 = -1, best2 = -1;
        for (var c = 0; c < 16; ++c) {
          if (rowFreq[c] > best1) {
            best2 = best1; ink2 = ink1;
            best1 = rowFreq[c]; ink1 = c;
          } else if (rowFreq[c] > best2) {
            best2 = rowFreq[c]; ink2 = c;
          }
        }

        // Row 0, column 0's low nibble is the background register itself.
        if (y == 0 && cellX == 0)
          ink2 = background;

        screenData[y * cellsAcross + cellX] = (byte)((ink1 << 4) | ink2);

        byte rowByte = 0;
        for (var px = 0; px < 4; ++px) {
          var color = colorAt[y, cellX * 4 + px];
          var pattern = color == background ? 0
            : color == ink1 ? 1
            : color == ink2 ? 2
            : color == color3 ? 3
            : _NearestPatternSlot(color, background, ink1, ink2, color3);
          rowByte |= (byte)(pattern << ((3 - px) * 2));
        }

        bitmap[cellIndex * 8 + py] = rowByte;
      }
    }

    return new() { BitmapData = bitmap, ScreenData = screenData, ColorRam = colorRam, Padding = new byte[PaddingSize] };
  }

  private static int _NearestPatternSlot(int color, int p0, int p1, int p2, int p3) {
    Span<int> slots = [p0, p1, p2, p3];
    var target = Commodore64Graphics.HexColors[color];
    int tr = (target >> 16) & 0xFF, tg = (target >> 8) & 0xFF, tb = target & 0xFF;

    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < slots.Length; ++i) {
      var c = Commodore64Graphics.HexColors[slots[i]];
      int dr = ((c >> 16) & 0xFF) - tr, dg = ((c >> 8) & 0xFF) - tg, db = (c & 0xFF) - tb;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;
      bestDistance = distance;
      best = i;
    }

    return best;
  }
}
