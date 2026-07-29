using System;
using FileFormat.Core;

namespace FileFormat.Flip64;

/// <summary>In-memory representation of a C64 Flip interlaced multicolor image.</summary>
public readonly record struct Flip64File : IImageFormatReader<Flip64File>, IImageToRawImage<Flip64File>, IImageFormatWriter<Flip64File> {

  static string IImageFormatMetadata<Flip64File>.PrimaryExtension => ".fbi";
  static string[] IImageFormatMetadata<Flip64File>.FileExtensions => [".fbi"];
  static Flip64File IImageFormatReader<Flip64File>.FromSpan(ReadOnlySpan<byte> data) => Flip64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Flip64File>.ToBytes(Flip64File file) => Flip64Writer.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of each bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of each screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size (bitmap1 + screen1 + bitmap2 + screen2 + color).</summary>
  internal const int MinPayloadSize = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize + ColorRamSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Flip image to a platform-independent <see cref="RawImage"/> in Rgb24 format by blending two interlaced frames.</summary>
  public static RawImage ToRawImage(Flip64File file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    const int bitmap2Offset = BitmapSize + ScreenRamSize;
    const int screen1Offset = BitmapSize;
    const int screen2Offset = BitmapSize + ScreenRamSize + BitmapSize;
    const int colorOffset = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;

    var hasFullData = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByteOffset = cellIndex * 8 + byteInCell;
        var pixelInByte = x % 4;

        int color1Index, color2Index;

        if (hasFullData) {
          var bitmap1Byte = bitmapByteOffset < file.RawData.Length ? file.RawData[bitmapByteOffset] : (byte)0;
          var bitValue1 = (bitmap1Byte >> ((3 - pixelInByte) * 2)) & 0x03;

          var bitmap2ByteOffset = bitmap2Offset + bitmapByteOffset;
          var bitmap2Byte = bitmap2ByteOffset < file.RawData.Length ? file.RawData[bitmap2ByteOffset] : (byte)0;
          var bitValue2 = (bitmap2Byte >> ((3 - pixelInByte) * 2)) & 0x03;

          var screen1Byte = file.RawData[screen1Offset + cellIndex];
          var screen2Byte = file.RawData[screen2Offset + cellIndex];
          var colorByte = file.RawData[colorOffset + cellIndex];

          color1Index = bitValue1 switch {
            0 => 0,
            1 => (screen1Byte >> 4) & 0x0F,
            2 => screen1Byte & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };

          color2Index = bitValue2 switch {
            0 => 0,
            1 => (screen2Byte >> 4) & 0x0F,
            2 => screen2Byte & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };
        } else {
          var bitmapByte = bitmapByteOffset < file.RawData.Length ? file.RawData[bitmapByteOffset] : (byte)0;
          var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;
          color1Index = bitValue != 0 ? 1 : 0;
          color2Index = color1Index;
        }

        var c1 = _C64Palette[color1Index];
        var c2 = _C64Palette[color2Index];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((((c1 >> 16) & 0xFF) + ((c2 >> 16) & 0xFF)) / 2);
        rgb[offset + 1] = (byte)((((c1 >> 8) & 0xFF) + ((c2 >> 8) & 0xFF)) / 2);
        rgb[offset + 2] = (byte)(((c1 & 0xFF) + (c2 & 0xFF)) / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
