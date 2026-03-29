using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mcs;

/// <summary>In-memory representation of a C64 Mcs (.mcs) multicolor screen image.</summary>
public sealed class McsFile : IImageFileFormat<McsFile> {

  static string IImageFileFormat<McsFile>.PrimaryExtension => ".mcs";
  static string[] IImageFileFormat<McsFile>.FileExtensions => [".mcs"];
  static McsFile IImageFileFormat<McsFile>.FromFile(FileInfo file) => McsReader.FromFile(file);
  static McsFile IImageFileFormat<McsFile>.FromBytes(byte[] data) => McsReader.FromBytes(data);
  static McsFile IImageFileFormat<McsFile>.FromStream(Stream stream) => McsReader.FromStream(stream);
  static byte[] IImageFileFormat<McsFile>.ToBytes(McsFile file) => McsWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the background color field in bytes.</summary>
  internal const int BackgroundColorSize = 1;

  /// <summary>Minimum raw payload size: bitmap + screen + color + background.</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenDataSize + ColorDataSize + BackgroundColorSize; // 10001

  /// <summary>Minimum file size: load address + payload.</summary>
  internal const int MinFileSize = LoadAddressSize + MinPayloadSize; // 10003

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int ImageWidth = 160;

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

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; } = [];

  /// <summary>Background color index (0-15). Bit-pair 0 maps to this color.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; } = [];

  /// <summary>Converts this Mcs image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(McsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitPair switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenData[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenData[cellIndex] & 0x0F,
          3 => file.ColorData[cellIndex] & 0x0F,
          _ => 0
        };

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

  /// <summary>Creates a Mcs image from a <see cref="RawImage"/>. Not supported.</summary>
  public static McsFile FromRawImage(RawImage image) => throw new NotSupportedException("Creating Mcs files from raw images is not supported.");
}
