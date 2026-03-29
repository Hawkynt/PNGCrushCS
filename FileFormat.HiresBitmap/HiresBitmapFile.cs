using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresBitmap;

/// <summary>In-memory representation of a C64 Hires Bitmap (.hbm) image.</summary>
public sealed class HiresBitmapFile : IImageFileFormat<HiresBitmapFile> {

  static string IImageFileFormat<HiresBitmapFile>.PrimaryExtension => ".hbm";
  static string[] IImageFileFormat<HiresBitmapFile>.FileExtensions => [".hbm", ".hir"];
  static HiresBitmapFile IImageFileFormat<HiresBitmapFile>.FromFile(FileInfo file) => HiresBitmapReader.FromFile(file);
  static HiresBitmapFile IImageFileFormat<HiresBitmapFile>.FromBytes(byte[] data) => HiresBitmapReader.FromBytes(data);
  static HiresBitmapFile IImageFileFormat<HiresBitmapFile>.FromStream(Stream stream) => HiresBitmapReader.FromStream(stream);
  static byte[] IImageFileFormat<HiresBitmapFile>.ToBytes(HiresBitmapFile file) => HiresBitmapWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum raw payload size: bitmap + screen.</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenDataSize; // 9000

  /// <summary>Minimum file size: load address + payload.</summary>
  internal const int MinFileSize = LoadAddressSize + MinPayloadSize; // 9002

  /// <summary>Image width in pixels, always 320 (hires).</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM / video matrix (1000 bytes).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; } = [];

  /// <summary>Converts this Hires Bitmap image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiresBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = ImageWidth;
    const int height = ImageHeight;
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

  /// <summary>Creates a Hires Bitmap image from a <see cref="RawImage"/>. Not supported.</summary>
  public static HiresBitmapFile FromRawImage(RawImage image) => throw new NotSupportedException("Creating HiresBitmap files from raw images is not supported.");
}
