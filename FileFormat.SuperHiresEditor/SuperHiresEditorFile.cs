using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor;

/// <summary>In-memory representation of a C64 Super Hires Editor (.she) interlace hires image.</summary>
public sealed class SuperHiresEditorFile : IImageFileFormat<SuperHiresEditorFile> {

  static string IImageFileFormat<SuperHiresEditorFile>.PrimaryExtension => ".she";
  static string[] IImageFileFormat<SuperHiresEditorFile>.FileExtensions => [".she"];
  static SuperHiresEditorFile IImageFileFormat<SuperHiresEditorFile>.FromFile(FileInfo file) => SuperHiresEditorReader.FromFile(file);
  static SuperHiresEditorFile IImageFileFormat<SuperHiresEditorFile>.FromBytes(byte[] data) => SuperHiresEditorReader.FromBytes(data);
  static SuperHiresEditorFile IImageFileFormat<SuperHiresEditorFile>.FromStream(Stream stream) => SuperHiresEditorReader.FromStream(stream);
  static byte[] IImageFileFormat<SuperHiresEditorFile>.ToBytes(SuperHiresEditorFile file) => SuperHiresEditorWriter.ToBytes(file);

  /// <summary>Size of one bitmap section in bytes (320x200 / 8 = 8000).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of one screen RAM section in bytes (40x25 = 1000).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum raw payload size: bitmap1 + screen1 + bitmap2 + screen2.</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenDataSize + BitmapDataSize + ScreenDataSize; // 18000

  /// <summary>Image width in pixels, always 320.</summary>
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

  /// <summary>Bitmap data for frame 1 (8000 bytes).</summary>
  public byte[] Bitmap1 { get; init; } = [];

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] Screen1 { get; init; } = [];

  /// <summary>Bitmap data for frame 2 (8000 bytes).</summary>
  public byte[] Bitmap2 { get; init; } = [];

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] Screen2 { get; init; } = [];

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; } = [];

  /// <summary>Converts this Super Hires Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging the two interlace frames.</summary>
  public static RawImage ToRawImage(SuperHiresEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var color1 = _DecodeHiresPixel(file.Bitmap1, file.Screen1, x, y);
        var color2 = _DecodeHiresPixel(file.Bitmap2, file.Screen2, x, y);

        // Average the two frames for interlace blending
        var r = ((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF);
        var g = ((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF);
        var b = (color1 & 0xFF) + (color2 & 0xFF);

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(r / 2);
        rgb[offset + 1] = (byte)(g / 2);
        rgb[offset + 2] = (byte)(b / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a Super Hires Editor image from a <see cref="RawImage"/>. Not supported.</summary>
  public static SuperHiresEditorFile FromRawImage(RawImage image) => throw new NotSupportedException("Creating Super Hires Editor files from raw images is not supported.");

  /// <summary>Decodes a single hires pixel from bitmap + screen data and returns the C64 palette color as 0xRRGGBB.</summary>
  private static int _DecodeHiresPixel(byte[] bitmap, byte[] screen, int x, int y) {
    var cellX = x / 8;
    var cellY = y / 8;
    var cellIndex = cellY * 40 + cellX;
    var byteInCell = y % 8;
    var bitmapByte = bitmap[cellIndex * 8 + byteInCell];
    var bitPosition = 7 - (x % 8);
    var bitValue = (bitmapByte >> bitPosition) & 1;

    var screenByte = screen[cellIndex];
    var colorIndex = bitValue == 1
      ? (screenByte >> 4) & 0x0F
      : screenByte & 0x0F;

    return _C64Palette[colorIndex];
  }
}
