using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CreateWithGarfield;

/// <summary>In-memory representation of a Commodore 64 Create with Garfield hires image.</summary>
public sealed class CreateWithGarfieldFile : IImageFileFormat<CreateWithGarfieldFile> {

  static string IImageFileFormat<CreateWithGarfieldFile>.PrimaryExtension => ".cwg";
  static string[] IImageFileFormat<CreateWithGarfieldFile>.FileExtensions => [".cwg"];
  static CreateWithGarfieldFile IImageFileFormat<CreateWithGarfieldFile>.FromFile(FileInfo file) => CreateWithGarfieldReader.FromFile(file);
  static CreateWithGarfieldFile IImageFileFormat<CreateWithGarfieldFile>.FromBytes(byte[] data) => CreateWithGarfieldReader.FromBytes(data);
  static CreateWithGarfieldFile IImageFileFormat<CreateWithGarfieldFile>.FromStream(Stream stream) => CreateWithGarfieldReader.FromStream(stream);
  static byte[] IImageFileFormat<CreateWithGarfieldFile>.ToBytes(CreateWithGarfieldFile file) => CreateWithGarfieldWriter.ToBytes(file);

  /// <summary>The fixed width of a Create with Garfield image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Create with Garfield image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 9003;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the border color byte.</summary>
  internal const int BorderColorSize = 1;

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
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM (1000 bytes, upper nybble = foreground color, lower nybble = background color per cell).</summary>
  public byte[] ScreenRam { get; init; } = [];

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Converts this Create with Garfield image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CreateWithGarfieldFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Not supported. Create with Garfield images have complex cell-based color constraints.</summary>
  public static CreateWithGarfieldFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CreateWithGarfieldFile is not supported due to complex cell-based color constraints.");
  }
}
