using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CDUPaint;

/// <summary>In-memory representation of a Commodore 64 CDU-Paint multicolor image.</summary>
public sealed class CDUPaintFile : IImageFileFormat<CDUPaintFile> {

  static string IImageFileFormat<CDUPaintFile>.PrimaryExtension => ".cdu";
  static string[] IImageFileFormat<CDUPaintFile>.FileExtensions => [".cdu"];
  static CDUPaintFile IImageFileFormat<CDUPaintFile>.FromFile(FileInfo file) => CDUPaintReader.FromFile(file);
  static CDUPaintFile IImageFileFormat<CDUPaintFile>.FromBytes(byte[] data) => CDUPaintReader.FromBytes(data);
  static CDUPaintFile IImageFileFormat<CDUPaintFile>.FromStream(Stream stream) => CDUPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<CDUPaintFile>.ToBytes(CDUPaintFile file) => CDUPaintWriter.ToBytes(file);

  /// <summary>The fixed width of a CDU-Paint image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a CDU-Paint image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10003;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

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

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; } = [];

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this CDU-Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CDUPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
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
          0 => file.BackgroundColor & 0x0F,
          1 => (file.VideoMatrix[cellIndex] >> 4) & 0x0F,
          2 => file.VideoMatrix[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
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

  /// <summary>Not supported. CDU-Paint images have complex cell-based color constraints.</summary>
  public static CDUPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CDUPaintFile is not supported due to complex cell-based color constraints.");
  }
}
