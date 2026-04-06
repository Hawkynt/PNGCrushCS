using System;
using FileFormat.Core;

namespace FileFormat.GigaPaint;

/// <summary>In-memory representation of a GigaPaint hires image.</summary>
public readonly record struct GigaPaintFile : IImageFormatReader<GigaPaintFile>, IImageToRawImage<GigaPaintFile>, IImageFormatWriter<GigaPaintFile> {

  static string IImageFormatMetadata<GigaPaintFile>.PrimaryExtension => ".gih";
  static string[] IImageFormatMetadata<GigaPaintFile>.FileExtensions => [".gih", ".gig"];
  static GigaPaintFile IImageFormatReader<GigaPaintFile>.FromSpan(ReadOnlySpan<byte> data) => GigaPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<GigaPaintFile>.ToBytes(GigaPaintFile file) => GigaPaintWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum bitmap data size in the payload.</summary>
  internal const int MinBitmapSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

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

  /// <summary>Converts this GigaPaint image to a platform-independent <see cref="RawImage"/> in Rgb24 format using simplified hires decode.</summary>
  public static RawImage ToRawImage(GigaPaintFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasScreen = file.RawData.Length >= MinBitmapSize + ScreenRamSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;
        var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        int colorIndex;
        if (hasScreen) {
          var screenByte = file.RawData[MinBitmapSize + cellIndex];
          colorIndex = bitValue == 1
            ? (screenByte >> 4) & 0x0F
            : screenByte & 0x0F;
        } else
          colorIndex = bitValue == 1 ? 1 : 0;

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
