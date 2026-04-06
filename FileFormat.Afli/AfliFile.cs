using System;
using FileFormat.Core;

namespace FileFormat.Afli;

/// <summary>In-memory representation of an AFLI (Advanced FLI) hires image for the Commodore 64.</summary>
public readonly record struct AfliFile : IImageFormatReader<AfliFile>, IImageToRawImage<AfliFile>, IImageFormatWriter<AfliFile> {

  static string IImageFormatMetadata<AfliFile>.PrimaryExtension => ".afl";
  static string[] IImageFormatMetadata<AfliFile>.FileExtensions => [".afl"];
  static AfliFile IImageFormatReader<AfliFile>.FromSpan(ReadOnlySpan<byte> data) => AfliReader.FromSpan(data);
  static byte[] IImageFormatWriter<AfliFile>.ToBytes(AfliFile file) => AfliWriter.ToBytes(file);

  /// <summary>Image width in pixels, always 320.</summary>
  public const int FixedWidth = 320;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int FixedHeight = 200;

  /// <summary>Expected file size: 2 load address + 9216 raw data = 9218 bytes.</summary>
  public const int ExpectedFileSize = 9218;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the raw FLI data section in bytes.</summary>
  internal const int RawDataSize = 9216;

  /// <summary>Size of the bitmap data section (8000 useful bytes from 8192 interleaved).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section (1000 bytes).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw FLI data (9216 bytes: bitmap + screen banks + padding).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this AFLI image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AfliFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    // AFLI hires: first 8000 bytes = bitmap, next 1000 bytes = screen RAM
    var bitmapData = file.RawData;
    var screenOffset = BitmapDataSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = bitmapData[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = screenOffset + cellIndex < file.RawData.Length
          ? file.RawData[screenOffset + cellIndex]
          : (byte)0;

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

}
