using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>In-memory representation of a C64 Hires Interlace by Feniks (.hlf) image.</summary>
public sealed class HiresInterlaceFeniksFile : IImageFileFormat<HiresInterlaceFeniksFile> {

  static string IImageFileFormat<HiresInterlaceFeniksFile>.PrimaryExtension => ".hlf";
  static string[] IImageFileFormat<HiresInterlaceFeniksFile>.FileExtensions => [".hlf"];
  static HiresInterlaceFeniksFile IImageFileFormat<HiresInterlaceFeniksFile>.FromFile(FileInfo file) => HiresInterlaceFeniksReader.FromFile(file);
  static HiresInterlaceFeniksFile IImageFileFormat<HiresInterlaceFeniksFile>.FromBytes(byte[] data) => HiresInterlaceFeniksReader.FromBytes(data);
  static HiresInterlaceFeniksFile IImageFileFormat<HiresInterlaceFeniksFile>.FromStream(Stream stream) => HiresInterlaceFeniksReader.FromStream(stream);
  static byte[] IImageFileFormat<HiresInterlaceFeniksFile>.ToBytes(HiresInterlaceFeniksFile file) => HiresInterlaceFeniksWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a single bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of a single screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of a single hires frame (bitmap + screen) in bytes.</summary>
  internal const int FrameSize = BitmapDataSize + ScreenRamSize;

  /// <summary>Minimum payload size in bytes (bitmap1 + screen1 + bitmap2 + screen2).</summary>
  internal const int MinPayloadSize = FrameSize * 2;

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
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this Hires Interlace image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging both hires frames.</summary>
  public static RawImage ToRawImage(HiresInterlaceFeniksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasBothFrames = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitPosition = 7 - (x % 8);

        var color1 = _DecodeHiresPixel(file.RawData, 0, cellIndex, byteInCell, bitPosition);
        int r, g, b;

        if (hasBothFrames) {
          var color2 = _DecodeHiresPixel(file.RawData, FrameSize, cellIndex, byteInCell, bitPosition);
          r = ((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF);
          g = ((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF);
          b = (color1 & 0xFF) + (color2 & 0xFF);
          r /= 2;
          g /= 2;
          b /= 2;
        } else {
          r = (color1 >> 16) & 0xFF;
          g = (color1 >> 8) & 0xFF;
          b = color1 & 0xFF;
        }

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

  /// <summary>Decodes a single hires pixel from a frame at the given base offset.</summary>
  private static int _DecodeHiresPixel(byte[] rawData, int frameOffset, int cellIndex, int byteInCell, int bitPosition) {
    var bitmapOffset = frameOffset + cellIndex * 8 + byteInCell;
    var bitmapByte = bitmapOffset < rawData.Length ? rawData[bitmapOffset] : (byte)0;
    var bitValue = (bitmapByte >> bitPosition) & 1;

    var screenOffset = frameOffset + BitmapDataSize + cellIndex;
    int colorIndex;
    if (screenOffset < rawData.Length) {
      var screenByte = rawData[screenOffset];
      colorIndex = bitValue == 1
        ? (screenByte >> 4) & 0x0F
        : screenByte & 0x0F;
    } else
      colorIndex = bitValue == 1 ? 1 : 0;

    return _C64Palette[colorIndex];
  }

  /// <summary>Not supported. Hires Interlace images have complex dual-frame color constraints.</summary>
  public static HiresInterlaceFeniksFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to HiresInterlaceFeniksFile is not supported due to complex dual-frame color constraints.");
  }
}
