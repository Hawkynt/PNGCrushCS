using System;
using FileFormat.Core;

namespace FileFormat.CfliDesigner;

/// <summary>In-memory representation of a C64 CFLI (Color FLI) multicolor image.</summary>
public readonly record struct CfliDesignerFile : IImageFormatReader<CfliDesignerFile>, IImageToRawImage<CfliDesignerFile>, IImageFormatWriter<CfliDesignerFile> {

  static string IImageFormatMetadata<CfliDesignerFile>.PrimaryExtension => ".cfli";
  static string[] IImageFormatMetadata<CfliDesignerFile>.FileExtensions => [".cfli"];
  static CfliDesignerFile IImageFormatReader<CfliDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => CfliDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<CfliDesignerFile>.ToBytes(CfliDesignerFile file) => CfliDesignerWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Number of screen RAM banks (one per char row group for FLI).</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of each screen RAM bank in bytes.</summary>
  internal const int ScreenBankSize = 1000;

  /// <summary>Total size of all screen RAM banks.</summary>
  internal const int TotalScreenSize = ScreenBankCount * ScreenBankSize;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size (bitmap + 8 screens + color).</summary>
  internal const int MinPayloadSize = BitmapSize + TotalScreenSize + ColorRamSize;

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

  /// <summary>Converts this CFLI image to a platform-independent <see cref="RawImage"/> in Rgb24 format using FLI multicolor decode.</summary>
  public static RawImage ToRawImage(CfliDesignerFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasFullData = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;
        var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        int colorIndex;
        if (hasFullData) {
          var screenBank = byteInCell % ScreenBankCount;
          var screenOffset = BitmapSize + screenBank * ScreenBankSize + cellIndex;
          var screenByte = screenOffset < file.RawData.Length ? file.RawData[screenOffset] : (byte)0;
          var colorOffset = BitmapSize + TotalScreenSize + cellIndex;
          var colorByte = colorOffset < file.RawData.Length ? file.RawData[colorOffset] : (byte)0;

          colorIndex = bitValue switch {
            0 => 0,
            1 => (screenByte >> 4) & 0x0F,
            2 => screenByte & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };
        } else
          colorIndex = bitValue != 0 ? 1 : 0;

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
