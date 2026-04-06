using System;
using FileFormat.Core;

namespace FileFormat.SuperHires;

/// <summary>In-memory representation of a Super Hires (C64 interlace hires) image.</summary>
public readonly record struct SuperHiresFile : IImageFormatReader<SuperHiresFile>, IImageToRawImage<SuperHiresFile>, IImageFormatWriter<SuperHiresFile> {

  static string IImageFormatMetadata<SuperHiresFile>.PrimaryExtension => ".shi";
  static string[] IImageFormatMetadata<SuperHiresFile>.FileExtensions => [".shi"];
  static SuperHiresFile IImageFormatReader<SuperHiresFile>.FromSpan(ReadOnlySpan<byte> data) => SuperHiresReader.FromSpan(data);
  static byte[] IImageFormatWriter<SuperHiresFile>.ToBytes(SuperHiresFile file) => SuperHiresWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height in pixels.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the padding/extra data at the end.</summary>
  internal const int PaddingSize = 240;

  /// <summary>Expected file size: 2 + 8000 + 1000 + 8000 + 1000 + 240 = 18242.</summary>
  public const int ExpectedFileSize = LoadAddressSize + BitmapDataSize + ScreenDataSize + BitmapDataSize + ScreenDataSize + PaddingSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data for frame 1 (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; }

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] ScreenData1 { get; init; }

  /// <summary>Bitmap data for frame 2 (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; }

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] ScreenData2 { get; init; }

  /// <summary>Trailing padding/extra data.</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Super Hires image to a platform-independent <see cref="RawImage"/> in Rgb24 format by blending two interlace frames.</summary>
  public static RawImage ToRawImage(SuperHiresFile file) {

    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
      for (var x = 0; x < ImageWidth; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitPosition = 7 - (x % 8);

        // Decode frame 1
        var bitmapByte1 = file.BitmapData1[cellIndex * 8 + byteInCell];
        var bitValue1 = (bitmapByte1 >> bitPosition) & 1;
        var screenByte1 = file.ScreenData1[cellIndex];
        var colorIndex1 = bitValue1 == 1
          ? (screenByte1 >> 4) & 0x0F
          : screenByte1 & 0x0F;

        // Decode frame 2
        var bitmapByte2 = file.BitmapData2[cellIndex * 8 + byteInCell];
        var bitValue2 = (bitmapByte2 >> bitPosition) & 1;
        var screenByte2 = file.ScreenData2[cellIndex];
        var colorIndex2 = bitValue2 == 1
          ? (screenByte2 >> 4) & 0x0F
          : screenByte2 & 0x0F;

        var color1 = _C64Palette[colorIndex1];
        var color2 = _C64Palette[colorIndex2];

        // Combine: same color = solid, different = average
        int r, g, b;
        if (colorIndex1 == colorIndex2) {
          r = (color1 >> 16) & 0xFF;
          g = (color1 >> 8) & 0xFF;
          b = color1 & 0xFF;
        } else {
          r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2;
          g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2;
          b = ((color1 & 0xFF) + (color2 & 0xFF)) / 2;
        }

        var offset = (y * ImageWidth + x) * 3;
        rgb[offset] = (byte)r;
        rgb[offset + 1] = (byte)g;
        rgb[offset + 2] = (byte)b;
      }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
