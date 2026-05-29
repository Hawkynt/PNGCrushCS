using System;
using FileFormat.Core;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>In-memory representation of a C64 Interlace Hires Editor image (two hires frames blended).</summary>
public readonly record struct InterlaceHiresEditorFile : IImageFormatReader<InterlaceHiresEditorFile>, IImageToRawImage<InterlaceHiresEditorFile>, IImageFormatWriter<InterlaceHiresEditorFile> {

  static string IImageFormatMetadata<InterlaceHiresEditorFile>.PrimaryExtension => ".ihe";
  static string[] IImageFormatMetadata<InterlaceHiresEditorFile>.FileExtensions => [".ihe"];
  static InterlaceHiresEditorFile IImageFormatReader<InterlaceHiresEditorFile>.FromSpan(ReadOnlySpan<byte> data) => InterlaceHiresEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterlaceHiresEditorFile>.ToBytes(InterlaceHiresEditorFile file) => InterlaceHiresEditorWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of one bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of one screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Minimum payload size in bytes (bitmap1 + screen1 + bitmap2 + screen2).</summary>
  internal const int MinPayloadSize = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Interlace Hires Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging two hires frames.</summary>
  public static RawImage ToRawImage(InterlaceHiresEditorFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasScreen1 = file.RawData.Length >= BitmapSize + ScreenRamSize;
    var hasFrame2 = file.RawData.Length >= BitmapSize + ScreenRamSize + BitmapSize;
    var hasScreen2 = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;
        var bitPosition = 7 - (x % 8);

        // Frame 1: hires decode
        var bitmapByte1 = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var bitValue1 = (bitmapByte1 >> bitPosition) & 1;

        int colorIndex1;
        if (hasScreen1) {
          var screenByte1 = file.RawData[BitmapSize + cellIndex];
          colorIndex1 = bitValue1 == 1
            ? (screenByte1 >> 4) & 0x0F
            : screenByte1 & 0x0F;
        } else
          colorIndex1 = bitValue1 == 1 ? 1 : 0;

        // Frame 2: hires decode
        var bitmap2Start = BitmapSize + ScreenRamSize;
        var bitmapByte2 = hasFrame2 && bitmap2Start + bitmapOffset < file.RawData.Length
          ? file.RawData[bitmap2Start + bitmapOffset]
          : (byte)0;
        var bitValue2 = (bitmapByte2 >> bitPosition) & 1;

        int colorIndex2;
        if (hasScreen2) {
          var screenByte2 = file.RawData[BitmapSize + ScreenRamSize + BitmapSize + cellIndex];
          colorIndex2 = bitValue2 == 1
            ? (screenByte2 >> 4) & 0x0F
            : screenByte2 & 0x0F;
        } else
          colorIndex2 = bitValue2 == 1 ? 1 : 0;

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

}
