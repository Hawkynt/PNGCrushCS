using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresC64;

/// <summary>In-memory representation of a Commodore 64 bare hires monochrome bitmap.</summary>
public sealed class HiresC64File : IImageFileFormat<HiresC64File> {

  static string IImageFileFormat<HiresC64File>.PrimaryExtension => ".hir";
  static string[] IImageFileFormat<HiresC64File>.FileExtensions => [".hir", ".hbm"];
  static FormatCapability IImageFileFormat<HiresC64File>.Capabilities => FormatCapability.MonochromeOnly;
  static HiresC64File IImageFileFormat<HiresC64File>.FromFile(FileInfo file) => HiresC64Reader.FromFile(file);
  static HiresC64File IImageFileFormat<HiresC64File>.FromBytes(byte[] data) => HiresC64Reader.FromBytes(data);
  static HiresC64File IImageFileFormat<HiresC64File>.FromStream(Stream stream) => HiresC64Reader.FromStream(stream);
  static byte[] IImageFileFormat<HiresC64File>.ToBytes(HiresC64File file) => HiresC64Writer.ToBytes(file);

  /// <summary>The fixed width of a hires C64 image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a hires C64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (8000 bytes raw bitmap).</summary>
  public const int ExpectedFileSize = 8000;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw bitmap data (8000 bytes, 1 bit per pixel within 8x8 cells).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Converts this hires C64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiresC64File file) {
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

        var colorValue = bitValue == 1 ? (byte)0xFF : (byte)0x00;
        var offset = (y * width + x) * 3;
        rgb[offset] = colorValue;
        rgb[offset + 1] = colorValue;
        rgb[offset + 2] = colorValue;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Hires C64 images have cell-based bitmap layout.</summary>
  public static HiresC64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to HiresC64File is not supported due to cell-based bitmap layout constraints.");
  }
}
