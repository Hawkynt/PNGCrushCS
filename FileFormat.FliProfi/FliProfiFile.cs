using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliProfi;

/// <summary>In-memory representation of a FLI Profi (.fpr) C64 image with per-raster-line color switching.</summary>
public sealed class FliProfiFile : IImageFileFormat<FliProfiFile> {

  static string IImageFileFormat<FliProfiFile>.PrimaryExtension => ".fpr";
  static string[] IImageFileFormat<FliProfiFile>.FileExtensions => [".fpr"];
  static FliProfiFile IImageFileFormat<FliProfiFile>.FromFile(FileInfo file) => FliProfiReader.FromFile(file);
  static FliProfiFile IImageFileFormat<FliProfiFile>.FromBytes(byte[] data) => FliProfiReader.FromBytes(data);
  static FliProfiFile IImageFileFormat<FliProfiFile>.FromStream(Stream stream) => FliProfiReader.FromStream(stream);
  static RawImage IImageFileFormat<FliProfiFile>.ToRawImage(FliProfiFile file) => ToRawImage(file);
  static FliProfiFile IImageFileFormat<FliProfiFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<FliProfiFile>.ToBytes(FliProfiFile file) => FliProfiWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels (multicolor mode = 160 effective, but displayed as 160).</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Bitmap data size in bytes (40 columns x 200 rows / 8 pixels per byte for 2bpp multicolor = 8000).</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Number of screen RAM banks (one per raster line within a character cell).</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of one screen RAM bank in bytes.</summary>
  internal const int ScreenRamBankSize = 1000;

  /// <summary>Total screen RAM size (8 banks x 1000 bytes).</summary>
  internal const int TotalScreenRamSize = ScreenBankCount * ScreenRamBankSize;

  /// <summary>Color RAM size in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size (bitmap + 8 screen RAM banks + color RAM).</summary>
  internal const int MinPayloadSize = BitmapSize + TotalScreenRamSize + ColorRamSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160 (multicolor).</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>
  /// Converts this FLI Profi image to a platform-independent <see cref="RawImage"/> in Rgb24 format.
  /// Uses multicolor FLI decode: 2bpp pixels with per-raster-line screen RAM bank switching.
  /// </summary>
  public static RawImage ToRawImage(FliProfiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasFullPayload = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4; // multicolor: 4 pixels per byte
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;

        var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;

        // 2bpp multicolor: each pixel is 2 bits
        var bitPosition = 6 - ((x % 4) * 2);
        var pixelValue = (bitmapByte >> bitPosition) & 0x03;

        int colorIndex;
        if (hasFullPayload) {
          // FLI mode: screen RAM bank selected by raster line within cell (y % 8)
          var screenBank = byteInCell;
          var screenOffset = BitmapSize + screenBank * ScreenRamBankSize + cellIndex;
          var screenByte = screenOffset < file.RawData.Length ? file.RawData[screenOffset] : (byte)0;

          var colorRamOffset = BitmapSize + TotalScreenRamSize + cellIndex;
          var colorByte = colorRamOffset < file.RawData.Length ? file.RawData[colorRamOffset] : (byte)0;

          colorIndex = pixelValue switch {
            0 => 0, // background (black)
            1 => (screenByte >> 4) & 0x0F,
            2 => (screenByte) & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0,
          };
        } else
          colorIndex = pixelValue != 0 ? 1 : 0;

        var color = _C64Palette[colorIndex & 0x0F];
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

  /// <summary>Not supported. FLI Profi images have complex per-line color switching constraints.</summary>
  public static FliProfiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FliProfiFile is not supported due to complex FLI color switching constraints.");
  }
}
