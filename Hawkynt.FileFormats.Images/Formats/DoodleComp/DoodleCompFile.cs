using System;
using FileFormat.Core;

namespace FileFormat.DoodleComp;

/// <summary>In-memory representation of a Commodore 64 Doodle Compressed hires image.</summary>
public readonly record struct DoodleCompFile : IImageFormatReader<DoodleCompFile>, IImageToRawImage<DoodleCompFile>, IImageFormatWriter<DoodleCompFile> {

  static string IImageFormatMetadata<DoodleCompFile>.PrimaryExtension => ".jj";
  static string[] IImageFormatMetadata<DoodleCompFile>.FileExtensions => [".jj"];
  static DoodleCompFile IImageFormatReader<DoodleCompFile>.FromSpan(ReadOnlySpan<byte> data) => DoodleCompReader.FromSpan(data);
  static byte[] IImageFormatWriter<DoodleCompFile>.ToBytes(DoodleCompFile file) => DoodleCompWriter.ToBytes(file);

  /// <summary>The fixed width of a Doodle Compressed image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Doodle Compressed image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Total decompressed data size (bitmap + screen).</summary>
  internal const int DecompressedDataSize = BitmapDataSize + ScreenRamSize;

  /// <summary>Minimum file size: load address (2) + at least 1 byte of compressed data.</summary>
  internal const int MinimumFileSize = 3;

  /// <summary>The RLE escape byte used in Doodle compression.</summary>
  internal const byte RleEscapeByte = 0xFE;

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

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel within 8x8 cells).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper nybble = foreground color, lower nybble = background color per cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Converts this Doodle Compressed image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DoodleCompFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = file.ScreenRam[cellIndex];
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

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
