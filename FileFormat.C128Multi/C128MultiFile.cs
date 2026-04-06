using System;
using FileFormat.Core;

namespace FileFormat.C128Multi;

/// <summary>In-memory representation of a C128 multicolor image (10240 bytes: 8000 bitmap + 1000 screen + 1000 color + 240 spare).</summary>
public readonly record struct C128MultiFile : IImageFormatReader<C128MultiFile>, IImageToRawImage<C128MultiFile>, IImageFormatWriter<C128MultiFile> {

  static string IImageFormatMetadata<C128MultiFile>.PrimaryExtension => ".c1m";
  static string[] IImageFormatMetadata<C128MultiFile>.FileExtensions => [".c1m"];
  static C128MultiFile IImageFormatReader<C128MultiFile>.FromSpan(ReadOnlySpan<byte> data) => C128MultiReader.FromSpan(data);
  static byte[] IImageFormatWriter<C128MultiFile>.ToBytes(C128MultiFile file) => C128MultiWriter.ToBytes(file);

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = 10240;

  /// <summary>Size of the bitmap data section.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the spare area.</summary>
  internal const int SpareSize = 240;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 160;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Always 160.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel in 8x8 cells).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorData { get; init; }

  /// <summary>Background color index (0-15), stored in spare area.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>The fixed C64/C128 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Converts the C128 multicolor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(C128MultiFile file) {

    var rgb = new byte[PixelWidth * PixelHeight * 3];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < PixelWidth; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = cellIndex * 8 + byteInCell < file.BitmapData.Length
          ? file.BitmapData[cellIndex * 8 + byteInCell]
          : (byte)0;
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => cellIndex < file.ScreenData.Length ? (file.ScreenData[cellIndex] >> 4) & 0x0F : 0,
          2 => cellIndex < file.ScreenData.Length ? file.ScreenData[cellIndex] & 0x0F : 0,
          3 => cellIndex < file.ColorData.Length ? file.ColorData[cellIndex] & 0x0F : 0,
          _ => 0
        };

        var color = _C64Palette[colorIndex];
        var offset = (y * PixelWidth + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
