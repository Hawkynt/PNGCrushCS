using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiResEditor;

/// <summary>In-memory representation of a Hires Editor image (C64, 320x200, 1bpp cell-based).</summary>
public sealed class HiResEditorFile : IImageFileFormat<HiResEditorFile> {

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes (320x200 / 8 = 8000).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes (40x25 = 1000).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Padding bytes at end of file.</summary>
  internal const int PaddingSize = 7;

  /// <summary>Expected file size: 2 + 8000 + 1000 + 7 = 9009.</summary>
  public const int ExpectedFileSize = LoadAddressSize + BitmapDataSize + ScreenDataSize + PaddingSize;

  /// <summary>Default load address ($2000).</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  static string IImageFileFormat<HiResEditorFile>.PrimaryExtension => ".hre";
  static string[] IImageFileFormat<HiResEditorFile>.FileExtensions => [".hre"];
  static HiResEditorFile IImageFileFormat<HiResEditorFile>.FromFile(FileInfo file) => HiResEditorReader.FromFile(file);
  static HiResEditorFile IImageFileFormat<HiResEditorFile>.FromBytes(byte[] data) => HiResEditorReader.FromBytes(data);
  static HiResEditorFile IImageFileFormat<HiResEditorFile>.FromStream(Stream stream) => HiResEditorReader.FromStream(stream);
  static RawImage IImageFileFormat<HiResEditorFile>.ToRawImage(HiResEditorFile file) => ToRawImage(file);
  static HiResEditorFile IImageFileFormat<HiResEditorFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<HiResEditorFile>.ToBytes(HiResEditorFile file) => HiResEditorWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; } = DefaultLoadAddress;

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM / video matrix (1000 bytes). Upper nybble = foreground color, lower nybble = background color per 8x8 cell.</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Converts this Hires Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiResEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[PixelWidth * PixelHeight * 3];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < PixelWidth; ++x) {
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
        var offset = (y * PixelWidth + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Hires Editor images cannot be created from raw images.</summary>
  public static HiResEditorFile FromRawImage(RawImage image) => throw new NotSupportedException("Creating Hires Editor images from raw images is not supported.");
}
