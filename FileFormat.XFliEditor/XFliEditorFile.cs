using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.XFliEditor;

/// <summary>In-memory representation of a C64 X-FLI Editor (.xfl) extended FLI multicolor image.</summary>
public sealed class XFliEditorFile : IImageFileFormat<XFliEditorFile> {

  static string IImageFileFormat<XFliEditorFile>.PrimaryExtension => ".xfl";
  static string[] IImageFileFormat<XFliEditorFile>.FileExtensions => [".xfl"];
  static XFliEditorFile IImageFileFormat<XFliEditorFile>.FromFile(FileInfo file) => XFliEditorReader.FromFile(file);
  static XFliEditorFile IImageFileFormat<XFliEditorFile>.FromBytes(byte[] data) => XFliEditorReader.FromBytes(data);
  static XFliEditorFile IImageFileFormat<XFliEditorFile>.FromStream(Stream stream) => XFliEditorReader.FromStream(stream);
  static byte[] IImageFileFormat<XFliEditorFile>.ToBytes(XFliEditorFile file) => XFliEditorWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of one screen RAM bank in bytes.</summary>
  internal const int ScreenBankSize = 1000;

  /// <summary>Number of screen banks in FLI mode.</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of all screen banks combined.</summary>
  internal const int AllScreenBanksSize = ScreenBankSize * ScreenBankCount; // 8000

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum raw payload size: bitmap + 8 screen banks + color RAM.</summary>
  internal const int MinPayloadSize = BitmapDataSize + AllScreenBanksSize + ColorDataSize; // 17000

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

  /// <summary>8 screen RAM banks, each 1000 bytes. ScreenBanks[bank][cellIndex].</summary>
  public byte[][] ScreenBanks { get; init; } = [];

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; } = [];

  /// <summary>Background color index (0-15). Bit-pair 0 maps to this color.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; } = [];

  /// <summary>Converts this X-FLI Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(XFliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var bank = y % ScreenBankCount;
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var screenByte = file.ScreenBanks[bank][cellIndex];
        var colorIndex = bitPair switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          3 => file.ColorData[cellIndex] & 0x0F,
          _ => 0
        };

        var color = _C64Palette[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an X-FLI Editor image from a <see cref="RawImage"/>. Not supported.</summary>
  public static XFliEditorFile FromRawImage(RawImage image) => throw new NotSupportedException("Creating X-FLI Editor files from raw images is not supported.");
}
