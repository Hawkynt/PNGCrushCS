using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PaintMagic;

/// <summary>In-memory representation of a Paint Magic C64 multicolor image (Koala layout).</summary>
public sealed class PaintMagicFile : IImageFileFormat<PaintMagicFile> {

  static string IImageFileFormat<PaintMagicFile>.PrimaryExtension => ".pmg";
  static string[] IImageFileFormat<PaintMagicFile>.FileExtensions => [".pmg"];
  static PaintMagicFile IImageFileFormat<PaintMagicFile>.FromFile(FileInfo file) => PaintMagicReader.FromFile(file);
  static PaintMagicFile IImageFileFormat<PaintMagicFile>.FromBytes(byte[] data) => PaintMagicReader.FromBytes(data);
  static PaintMagicFile IImageFileFormat<PaintMagicFile>.FromStream(Stream stream) => PaintMagicReader.FromStream(stream);
  static byte[] IImageFileFormat<PaintMagicFile>.ToBytes(PaintMagicFile file) => PaintMagicWriter.ToBytes(file);

  /// <summary>The fixed width of a Paint Magic image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Paint Magic image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + bitmapData(8000) + videoMatrix(1000) + colorRam(1000) + backgroundColor(1) = 10003.</summary>
  public const int ExpectedFileSize = 10003;

  internal const int BitmapDataSize = 8000;
  internal const int VideoMatrixSize = 1000;
  internal const int ColorRamSize = 1000;
  internal const int LoadAddressSize = 2;

  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Video matrix / screen RAM (1000 bytes).</summary>
  public byte[] VideoMatrix { get; init; } = [];

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Paint Magic image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(PaintMagicFile file) {
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

  /// <summary>Not supported.</summary>
  public static PaintMagicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to PaintMagicFile is not supported due to complex cell-based color constraints.");
  }
}
