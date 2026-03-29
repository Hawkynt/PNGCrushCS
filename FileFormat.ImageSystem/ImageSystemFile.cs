using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ImageSystem;

/// <summary>In-memory representation of a C64 Image System image (ISH hires / ISM multicolor).</summary>
public sealed class ImageSystemFile : IImageFileFormat<ImageSystemFile> {

  /// <summary>Size of bitmap data section.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of screen RAM section.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of color RAM section (multicolor only).</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of load address.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Hires file size: 2 + 8000 + 1000 + 7 padding = 9009.</summary>
  public const int HiresFileSize = 9009;

  /// <summary>Multicolor file size: same as Koala (2 + 8000 + 1000 + 1000 + 1) = 10003.</summary>
  public const int MulticolorFileSize = 10003;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  static string IImageFileFormat<ImageSystemFile>.PrimaryExtension => ".ish";
  static string[] IImageFileFormat<ImageSystemFile>.FileExtensions => [".ish", ".ism"];
  static ImageSystemFile IImageFileFormat<ImageSystemFile>.FromFile(FileInfo file) => ImageSystemReader.FromFile(file);
  static ImageSystemFile IImageFileFormat<ImageSystemFile>.FromBytes(byte[] data) => ImageSystemReader.FromBytes(data);
  static ImageSystemFile IImageFileFormat<ImageSystemFile>.FromStream(Stream stream) => ImageSystemReader.FromStream(stream);
  static RawImage IImageFileFormat<ImageSystemFile>.ToRawImage(ImageSystemFile file) => ToRawImage(file);
  static ImageSystemFile IImageFileFormat<ImageSystemFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<ImageSystemFile>.ToBytes(ImageSystemFile file) => ImageSystemWriter.ToBytes(file);

  /// <summary>Image width in pixels (320 for hires, 160 for multicolor).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, always 200.</summary>
  public int Height { get; init; }

  /// <summary>Whether this is a hires (true) or multicolor (false) image.</summary>
  public bool IsHires { get; init; }

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM (1000 bytes).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Color RAM (1000 bytes, multicolor only; null for hires).</summary>
  public byte[]? ColorData { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Image System file to an Rgb24 raw image.</summary>
  public static RawImage ToRawImage(ImageSystemFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.IsHires ? _HiresToRawImage(file) : _MultiToRawImage(file);
  }

  /// <summary>Not supported. Image System images have complex cell-based color constraints.</summary>
  public static ImageSystemFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ImageSystemFile is not supported.");
  }

  private static RawImage _HiresToRawImage(ImageSystemFile file) {
    const int width = 320;
    const int height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = cellIndex * 8 + byteInCell < file.BitmapData.Length
          ? file.BitmapData[cellIndex * 8 + byteInCell]
          : (byte)0;
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = cellIndex < file.ScreenData.Length ? file.ScreenData[cellIndex] : (byte)0;
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

        var color = _C64Palette[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static RawImage _MultiToRawImage(ImageSystemFile file) {
    const int width = 160;
    const int height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = cellIndex * 8 + byteInCell < file.BitmapData.Length
          ? file.BitmapData[cellIndex * 8 + byteInCell]
          : (byte)0;
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => cellIndex < file.ScreenData.Length ? (file.ScreenData[cellIndex] >> 4) & 0x0F : 0,
          2 => cellIndex < file.ScreenData.Length ? file.ScreenData[cellIndex] & 0x0F : 0,
          3 => file.ColorData != null && cellIndex < file.ColorData.Length ? file.ColorData[cellIndex] & 0x0F : 0,
          _ => 0
        };

        var color = _C64Palette[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
