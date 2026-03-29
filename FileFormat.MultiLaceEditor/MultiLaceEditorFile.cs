using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MultiLaceEditor;

/// <summary>In-memory representation of a C64 Multi-Lace Editor multicolor interlace image (two multicolor frames blended).</summary>
public sealed class MultiLaceEditorFile : IImageFileFormat<MultiLaceEditorFile> {

  static string IImageFileFormat<MultiLaceEditorFile>.PrimaryExtension => ".mle";
  static string[] IImageFileFormat<MultiLaceEditorFile>.FileExtensions => [".mle"];
  static MultiLaceEditorFile IImageFileFormat<MultiLaceEditorFile>.FromFile(FileInfo file) => MultiLaceEditorReader.FromFile(file);
  static MultiLaceEditorFile IImageFileFormat<MultiLaceEditorFile>.FromBytes(byte[] data) => MultiLaceEditorReader.FromBytes(data);
  static MultiLaceEditorFile IImageFileFormat<MultiLaceEditorFile>.FromStream(Stream stream) => MultiLaceEditorReader.FromStream(stream);
  static byte[] IImageFileFormat<MultiLaceEditorFile>.ToBytes(MultiLaceEditorFile file) => MultiLaceEditorWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of one bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of one screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size in bytes (bitmap1 + screen1 + bitmap2 + screen2 + color).</summary>
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
  public byte[] RawData { get; init; } = [];

  /// <summary>Decodes a single multicolor frame from the raw payload.</summary>
  private static int _DecodeMulticolorPixel(byte[] rawData, int bitmapStart, int screenStart, int colorStart, bool hasScreen, bool hasColor, int cellIndex, int byteInCell, int pixelInByte) {
    var bitmapOffset = bitmapStart + cellIndex * 8 + byteInCell;
    var bitmapByte = bitmapOffset < rawData.Length ? rawData[bitmapOffset] : (byte)0;
    var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

    if (!hasScreen)
      return bitValue != 0 ? 1 : 0;

    var screenByte = screenStart < rawData.Length ? rawData[screenStart + cellIndex] : (byte)0;
    var colorByte = hasColor && colorStart + cellIndex < rawData.Length ? rawData[colorStart + cellIndex] : (byte)0;

    return bitValue switch {
      0 => 0,
      1 => (screenByte >> 4) & 0x0F,
      2 => screenByte & 0x0F,
      3 => colorByte & 0x0F,
      _ => 0
    };
  }

  /// <summary>Converts this Multi-Lace Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging two multicolor frames.</summary>
  public static RawImage ToRawImage(MultiLaceEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    // Frame 1 layout: bitmap1(0..7999) + screen1(8000..8999)
    const int bitmap1Start = 0;
    const int screen1Start = BitmapSize;
    var hasScreen1 = file.RawData.Length >= BitmapSize + ScreenRamSize;

    // Frame 2 layout: bitmap2(9000..16999) + screen2(17000..17999)
    const int bitmap2Start = BitmapSize + ScreenRamSize;
    const int screen2Start = BitmapSize + ScreenRamSize + BitmapSize;
    var hasScreen2 = file.RawData.Length >= BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;

    // Shared color RAM: color(18000..18999)
    const int colorStart = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;
    var hasColor = file.RawData.Length >= MinPayloadSize;

    var hasFrame2 = file.RawData.Length >= BitmapSize + ScreenRamSize + BitmapSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var pixelInByte = x % 4;

        var colorIndex1 = _DecodeMulticolorPixel(file.RawData, bitmap1Start, screen1Start, colorStart, hasScreen1, hasColor, cellIndex, byteInCell, pixelInByte);

        int colorIndex2;
        if (hasFrame2)
          colorIndex2 = _DecodeMulticolorPixel(file.RawData, bitmap2Start, screen2Start, colorStart, hasScreen2, hasColor, cellIndex, byteInCell, pixelInByte);
        else
          colorIndex2 = colorIndex1;

        var color1 = _C64Palette[colorIndex1];
        var color2 = _C64Palette[colorIndex2];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2);
        rgb[offset + 1] = (byte)((((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2);
        rgb[offset + 2] = (byte)(((color1 & 0xFF) + (color2 & 0xFF)) / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Multi-Lace Editor images have complex multicolor interlace constraints.</summary>
  public static MultiLaceEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MultiLaceEditorFile is not supported due to complex multicolor interlace constraints.");
  }
}
