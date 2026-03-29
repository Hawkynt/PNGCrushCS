using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliGraph;

/// <summary>In-memory representation of a FLI Graph (FLI multicolor variant) image for the Commodore 64.</summary>
public sealed class FliGraphFile : IImageFileFormat<FliGraphFile> {

  static string IImageFileFormat<FliGraphFile>.PrimaryExtension => ".flg";
  static string[] IImageFileFormat<FliGraphFile>.FileExtensions => [".flg"];
  static FliGraphFile IImageFileFormat<FliGraphFile>.FromFile(FileInfo file) => FliGraphReader.FromFile(file);
  static FliGraphFile IImageFileFormat<FliGraphFile>.FromBytes(byte[] data) => FliGraphReader.FromBytes(data);
  static FliGraphFile IImageFileFormat<FliGraphFile>.FromStream(Stream stream) => FliGraphReader.FromStream(stream);
  static byte[] IImageFileFormat<FliGraphFile>.ToBytes(FliGraphFile file) => FliGraphWriter.ToBytes(file);

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int FixedWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int FixedHeight = 200;

  /// <summary>Expected file size: 2 + 8000 + 8000 + 1000 + 472 = 17474 bytes.</summary>
  public const int ExpectedFileSize = 17474;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the per-scanline screen data section in bytes (40 bytes x 200 lines).</summary>
  internal const int ScreenDataSize = 8000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 472;

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

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Per-scanline screen RAM (8000 bytes: 40 bytes per scanline x 200 lines).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Color RAM (1000 bytes, one per 4x8 cell).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Padding bytes at the end of the file (472 bytes).</summary>
  public byte[] Padding { get; init; } = [];

  /// <summary>Converts this FLI Graph image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FliGraphFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _FliMultiToRawImage(file.BitmapData, file.ScreenData, file.ColorRam);
  }

  /// <summary>Not supported. FLI multicolor images have complex per-scanline color constraints.</summary>
  public static FliGraphFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FliGraphFile is not supported due to complex per-scanline FLI color constraints.");
  }

  /// <summary>Shared FLI multicolor decode: per-scanline screen RAM instead of per-cell.</summary>
  private static RawImage _FliMultiToRawImage(byte[] bitmapData, byte[] screenData, byte[] colorRam) {
    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    // Background color derived from first screen byte
    var backgroundColor = screenData.Length > 0 ? screenData[0] & 0x0F : 0;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = bitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        // FLI uses per-scanline screen RAM: screenData[y * 40 + cellX]
        var screenIndex = y * 40 + cellX;
        var screenByte = screenIndex < screenData.Length ? screenData[screenIndex] : (byte)0;

        var colorIndex = bitValue switch {
          0 => backgroundColor,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          3 => cellIndex < colorRam.Length ? colorRam[cellIndex] & 0x0F : 0,
          _ => 0
        };

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
