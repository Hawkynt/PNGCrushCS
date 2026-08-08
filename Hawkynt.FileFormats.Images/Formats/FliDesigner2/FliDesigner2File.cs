using System;
using FileFormat.Core;

namespace FileFormat.FliDesigner2;

/// <summary>In-memory representation of a FLI Designer 2 (enhanced FLI multicolor) image for the Commodore 64.</summary>
public readonly record struct FliDesigner2File
  : IImageFormatReader<FliDesigner2File>, IImageToRawImage<FliDesigner2File>,
    IImageFromRawImage<FliDesigner2File>, IImageFormatWriter<FliDesigner2File> {

  static string IImageFormatMetadata<FliDesigner2File>.PrimaryExtension => ".fd2";
  static string[] IImageFormatMetadata<FliDesigner2File>.FileExtensions => [".fd2"];
  static FliDesigner2File IImageFormatReader<FliDesigner2File>.FromSpan(ReadOnlySpan<byte> data) => FliDesigner2Reader.FromSpan(data);
  static byte[] IImageFormatWriter<FliDesigner2File>.ToBytes(FliDesigner2File file) => FliDesigner2Writer.ToBytes(file);

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int FixedWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int FixedHeight = 200;

  /// <summary>Minimum file size: 2 + 8000 + 8000 + 1000 + 472 = 17474 bytes.</summary>
  public const int MinFileSize = 17474;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the per-scanline screen data section in bytes (40 bytes x 200 lines).</summary>
  internal const int ScreenDataSize = 8000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the base padding section in bytes.</summary>
  internal const int BasePaddingSize = 472;

  /// <summary>Character columns across the screen.</summary>
  internal const int Columns = 40;

  /// <summary>Character rows down the screen.</summary>
  internal const int Rows = FixedHeight / 8;

  /// <summary>Video matrices, one for each raster line of a character cell.</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>The entries one of those holds.</summary>
  internal const int ScreenBankSize = Columns * Rows;

  /// <summary>What pattern 00 shows, which the decoder recovers from the first screen byte.</summary>
  internal const int Background = 0;

  /// <summary>Default load address, putting the bitmap at $2000.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

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

  /// <summary>Extra data beyond the base FLI multicolor layout (variable length, may be empty).</summary>
  public byte[] ExtraData { get; init; }

  /// <summary>Converts this FLI Designer 2 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FliDesigner2File file) {
    return _FliMultiToRawImage(file.BitmapData, file.ScreenData, file.ColorRam);
  }

  /// <summary>Encodes a picture as an FLI Designer 2 screen, scaling it to 160x200 first.</summary>
  /// <remarks>
  /// The video matrix is stored a whole raster line at a time rather than in eight banks, so the
  /// shared encoder's banked output is scattered into that order afterwards — the two hold the same
  /// entries, only addressed differently, and writing the banked form straight out would put every
  /// row of cells but the first in the wrong place.
  /// <para/>
  /// <see cref="ToRawImage"/> takes the background colour from the low nibble of the very first
  /// screen byte, so that entry is not free: it has to say black, which is what pattern 00 is
  /// encoded as. The first cell's first raster line is therefore re-done afterwards with its second
  /// colour spent on the background, leaving it one free colour instead of two. Without that the
  /// picture would decode against a background nobody chose.
  /// </remarks>
  public static FliDesigner2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var banked = new byte[ScreenBankCount * ScreenBankSize];
    var colorRam = new byte[ColorRamSize];
    Commodore64Graphics.EncodeMulticolorFli(
      rgb, FixedWidth, FixedHeight, Background, bitmap, banked, ScreenBankSize, colorRam);

    var screen = new byte[ScreenDataSize];
    for (var row = 0; row < Rows; ++row)
    for (var line = 0; line < ScreenBankCount; ++line)
    for (var column = 0; column < Columns; ++column)
      screen[(row * ScreenBankCount + line) * Columns + column] = banked[line * ScreenBankSize + row * Columns + column];

    _PinBackgroundIntoFirstEntry(rgb, bitmap, screen, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      ScreenData = screen,
      ColorRam = colorRam,
      ExtraData = new byte[BasePaddingSize],
    };
  }

  /// <summary>Redoes the first cell's first raster line so that its second colour is the background.</summary>
  private static void _PinBackgroundIntoFirstEntry(
    ReadOnlySpan<byte> rgb, Span<byte> bitmap, Span<byte> screen, ReadOnlySpan<byte> colorRam) {
    var third = colorRam[0] & 0x0F;

    // One colour is free; the other three the line can show are already spoken for.
    var foreground = 0;
    var bestError = long.MaxValue;
    for (var candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
      long error = 0;
      for (var x = 0; x < 4; ++x) {
        var index = Commodore64Graphics.FindNearestColorIndex(rgb[x * 3], rgb[x * 3 + 1], rgb[x * 3 + 2]);
        error += Math.Min(_Distance(index, Background), Math.Min(_Distance(index, third), _Distance(index, candidate)));
      }

      if (error >= bestError)
        continue;

      bestError = error;
      foreground = candidate;
    }

    var row = 0;
    for (var x = 0; x < 4; ++x) {
      var index = Commodore64Graphics.FindNearestColorIndex(rgb[x * 3], rgb[x * 3 + 1], rgb[x * 3 + 2]);
      var pattern = 0;
      var best = _Distance(index, Background);
      if (_Distance(index, foreground) < best) {
        best = _Distance(index, foreground);
        pattern = 1;
      }

      if (_Distance(index, third) < best)
        pattern = 3;

      row |= pattern << ((3 - x) * 2);
    }

    bitmap[0] = (byte)row;
    screen[0] = (byte)((foreground << 4) | Background);
  }

  private static int _Distance(int left, int right) {
    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF), dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF), db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>Shared FLI multicolor decode: per-scanline screen RAM instead of per-cell.</summary>
  private static RawImage _FliMultiToRawImage(byte[] bitmapData, byte[] screenData, byte[] colorRam) {
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
}
