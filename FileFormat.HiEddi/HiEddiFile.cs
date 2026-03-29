using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiEddi;

/// <summary>In-memory representation of a HiEddi C64 hires image (Doodle layout).</summary>
public sealed class HiEddiFile : IImageFileFormat<HiEddiFile> {

  static string IImageFileFormat<HiEddiFile>.PrimaryExtension => ".hed";
  static string[] IImageFileFormat<HiEddiFile>.FileExtensions => [".hed"];
  static HiEddiFile IImageFileFormat<HiEddiFile>.FromFile(FileInfo file) => HiEddiReader.FromFile(file);
  static HiEddiFile IImageFileFormat<HiEddiFile>.FromBytes(byte[] data) => HiEddiReader.FromBytes(data);
  static HiEddiFile IImageFileFormat<HiEddiFile>.FromStream(Stream stream) => HiEddiReader.FromStream(stream);
  static byte[] IImageFileFormat<HiEddiFile>.ToBytes(HiEddiFile file) => HiEddiWriter.ToBytes(file);

  /// <summary>The fixed width of a HiEddi image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a HiEddi image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + bitmapData(8000) + screenRam(1000) + padding(216) = 9218.</summary>
  public const int ExpectedFileSize = 9218;

  internal const int BitmapDataSize = 8000;
  internal const int ScreenRamSize = 1000;
  internal const int LoadAddressSize = 2;
  internal const int PaddingSize = 216;

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

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = fg/bg color per 8x8 cell).</summary>
  public byte[] ScreenRam { get; init; } = [];

  /// <summary>Converts this HiEddi image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiEddiFile file) {
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
  public static HiEddiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to HiEddiFile is not supported due to complex cell-based color constraints.");
  }
}
