using System;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio;

/// <summary>In-memory representation of a C64 Interlace Studio (.ist) multicolor interlace image.</summary>
public readonly record struct InterlaceStudioFile : IImageFormatReader<InterlaceStudioFile>, IImageToRawImage<InterlaceStudioFile>, IImageFormatWriter<InterlaceStudioFile> {

  static string IImageFormatMetadata<InterlaceStudioFile>.PrimaryExtension => ".ist";
  static string[] IImageFormatMetadata<InterlaceStudioFile>.FileExtensions => [".ist"];
  static InterlaceStudioFile IImageFormatReader<InterlaceStudioFile>.FromSpan(ReadOnlySpan<byte> data) => InterlaceStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterlaceStudioFile>.ToBytes(InterlaceStudioFile file) => InterlaceStudioWriter.ToBytes(file);

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int ImageWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a single bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of a single screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Total file size: LoadAddress(2) + Bitmap1(8000) + Screen1(1000) + ColorData(1000) + Bitmap2(8000) + Screen2(1000) + BackgroundColor(1) = 19003.</summary>
  public const int FileSize = 19003;

  /// <summary>Minimum payload size (everything except load address).</summary>
  internal const int MinPayloadSize = FileSize - LoadAddressSize; // 19001

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>First frame bitmap data (8000 bytes).</summary>
  public byte[] Bitmap1 { get; init; }

  /// <summary>First frame screen RAM (1000 bytes).</summary>
  public byte[] Screen1 { get; init; }

  /// <summary>Shared color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; }

  /// <summary>Second frame bitmap data (8000 bytes).</summary>
  public byte[] Bitmap2 { get; init; }

  /// <summary>Second frame screen RAM (1000 bytes).</summary>
  public byte[] Screen2 { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Interlace Studio image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging both multicolor frames.</summary>
  public static RawImage ToRawImage(InterlaceStudioFile file) {

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var color1 = _DecodeMulticolorPixel(file.Bitmap1, file.Screen1, file.ColorData, file.BackgroundColor, x, y);
        var color2 = _DecodeMulticolorPixel(file.Bitmap2, file.Screen2, file.ColorData, file.BackgroundColor, x, y);

        var r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2;
        var g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2;
        var b = ((color1 & 0xFF) + (color2 & 0xFF)) / 2;

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)r;
        rgb[offset + 1] = (byte)g;
        rgb[offset + 2] = (byte)b;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Decodes a single multicolor pixel from the given frame data.</summary>
  private static int _DecodeMulticolorPixel(byte[] bitmapData, byte[] screenData, byte[] colorData, byte backgroundColor, int x, int y) {
    var cellX = x / 4;
    var cellY = y / 8;
    var cellIndex = cellY * 40 + cellX;
    var byteInCell = y % 8;

    var bitmapOffset = cellIndex * 8 + byteInCell;
    var bitmapByte = bitmapOffset < bitmapData.Length ? bitmapData[bitmapOffset] : (byte)0;
    var pixelInByte = x % 4;
    var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

    var colorIndex = bitPair switch {
      0 => backgroundColor & 0x0F,
      1 => cellIndex < screenData.Length ? (screenData[cellIndex] >> 4) & 0x0F : 0,
      2 => cellIndex < screenData.Length ? screenData[cellIndex] & 0x0F : 0,
      3 => cellIndex < colorData.Length ? colorData[cellIndex] & 0x0F : 0,
      _ => 0
    };

    return _C64Palette[colorIndex];
  }

}
