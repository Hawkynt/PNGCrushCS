using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FunPhotor;

/// <summary>In-memory representation of a Fun Photor C64 multicolor image.</summary>
public sealed class FunPhotorFile : IImageFileFormat<FunPhotorFile> {

  static string IImageFileFormat<FunPhotorFile>.PrimaryExtension => ".fpr";
  static string[] IImageFileFormat<FunPhotorFile>.FileExtensions => [".fpr"];
  static FunPhotorFile IImageFileFormat<FunPhotorFile>.FromFile(FileInfo file) => FunPhotorReader.FromFile(file);
  static FunPhotorFile IImageFileFormat<FunPhotorFile>.FromBytes(byte[] data) => FunPhotorReader.FromBytes(data);
  static FunPhotorFile IImageFileFormat<FunPhotorFile>.FromStream(Stream stream) => FunPhotorReader.FromStream(stream);
  static byte[] IImageFileFormat<FunPhotorFile>.ToBytes(FunPhotorFile file) => FunPhotorWriter.ToBytes(file);

  /// <summary>Fixed total file size: 2 + 8000 + 1000 + 1000 + 48 = 10050 bytes.</summary>
  internal const int ExpectedFileSize = 10050;

  /// <summary>Minimum valid file size (exact match required).</summary>
  public const int MinFileSize = ExpectedFileSize;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the reserved section in bytes.</summary>
  internal const int ReservedSize = 48;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => 160;

  /// <summary>Image height, always 200.</summary>
  public int Height => 200;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM (1000 bytes).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; } = [];

  /// <summary>Reserved bytes at end of file (48 bytes).</summary>
  public byte[] Reserved { get; init; } = [];

  /// <summary>Converts this Fun Photor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FunPhotorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 160;
    const int height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => 0,
          1 => (file.ScreenData[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenData[cellIndex] & 0x0F,
          3 => file.ColorData[cellIndex] & 0x0F,
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

  /// <summary>Not supported.</summary>
  public static FunPhotorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FunPhotorFile is not supported.");
  }
}
