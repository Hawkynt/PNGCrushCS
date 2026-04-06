using System;
using FileFormat.Core;

namespace FileFormat.HiresC64;

/// <summary>In-memory representation of a Commodore 64 bare hires monochrome bitmap.</summary>
public readonly record struct HiresC64File : IImageFormatReader<HiresC64File>, IImageToRawImage<HiresC64File>, IImageFormatWriter<HiresC64File> {

  static string IImageFormatMetadata<HiresC64File>.PrimaryExtension => ".hir";
  static string[] IImageFormatMetadata<HiresC64File>.FileExtensions => [".hir", ".hbm"];
  static HiresC64File IImageFormatReader<HiresC64File>.FromSpan(ReadOnlySpan<byte> data) => HiresC64Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HiresC64File>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<HiresC64File>.ToBytes(HiresC64File file) => HiresC64Writer.ToBytes(file);

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
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this hires C64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiresC64File file) {

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

}
