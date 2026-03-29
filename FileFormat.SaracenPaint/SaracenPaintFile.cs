using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SaracenPaint;

/// <summary>In-memory representation of a Saracen Paint C64 hires image (Art Studio hires layout).</summary>
public sealed class SaracenPaintFile : IImageFileFormat<SaracenPaintFile> {

  static string IImageFileFormat<SaracenPaintFile>.PrimaryExtension => ".sar";
  static string[] IImageFileFormat<SaracenPaintFile>.FileExtensions => [".sar"];
  static SaracenPaintFile IImageFileFormat<SaracenPaintFile>.FromFile(FileInfo file) => SaracenPaintReader.FromFile(file);
  static SaracenPaintFile IImageFileFormat<SaracenPaintFile>.FromBytes(byte[] data) => SaracenPaintReader.FromBytes(data);
  static SaracenPaintFile IImageFileFormat<SaracenPaintFile>.FromStream(Stream stream) => SaracenPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<SaracenPaintFile>.ToBytes(SaracenPaintFile file) => SaracenPaintWriter.ToBytes(file);

  /// <summary>The fixed width of a Saracen Paint image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Saracen Paint image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + screenRam(1000) + bitmapData(8000) + padding(7) = 9009.</summary>
  public const int ExpectedFileSize = 9009;

  internal const int ScreenRamSize = 1000;
  internal const int BitmapDataSize = 8000;
  internal const int LoadAddressSize = 2;
  internal const int PaddingSize = 7;

  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = fg/bg color per 8x8 cell).</summary>
  public byte[] ScreenRam { get; init; } = [];

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Converts this Saracen Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SaracenPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
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

        var screenByte = file.ScreenRam[cellIndex];
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

  /// <summary>Not supported.</summary>
  public static SaracenPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to SaracenPaintFile is not supported due to complex cell-based color constraints.");
  }
}
