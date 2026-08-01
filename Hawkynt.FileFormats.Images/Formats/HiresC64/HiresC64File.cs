using System;
using FileFormat.Core;

namespace FileFormat.HiresC64;

/// <summary>In-memory representation of a Commodore 64 bare hires monochrome bitmap.</summary>
public readonly record struct HiresC64File : IImageFormatReader<HiresC64File>, IImageToRawImage<HiresC64File>, IImageFromRawImage<HiresC64File>, IImageFormatWriter<HiresC64File> {

  static string IImageFormatMetadata<HiresC64File>.PrimaryExtension => ".hir";
    // .hpi as well: the reference decoder tries this format first for it and only falls through to
  // Hi-Pic Creator, and one extension resolves to one format here.
  static string[] IImageFormatMetadata<HiresC64File>.FileExtensions => [".hir", ".hbm", ".hpi"];
  static HiresC64File IImageFormatReader<HiresC64File>.FromSpan(ReadOnlySpan<byte> data) => HiresC64Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<HiresC64File>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<HiresC64File>.ToBytes(HiresC64File file) => HiresC64Writer.ToBytes(file);

  /// <summary>The fixed width of a hires C64 image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a hires C64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (8000 bytes raw bitmap).</summary>
  public const int ExpectedFileSize = 8002;

  /// <summary>Size of the bitmap.</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Where the bitmap starts, after the load address every C64 file carries.</summary>
  public const int BitmapOffset = 2;

  /// <summary>
  /// The attribute every cell uses. There is no video matrix in this format at all — the screen is
  /// one bit a pixel and the same two colours throughout, white over black, so the pair is fixed
  /// rather than stored.
  /// </summary>
  public const byte Attribute = 0x10;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw bitmap data (8000 bytes, 1 bit per pixel within 8x8 cells).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this hires C64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiresC64File file) {

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

        var colorValue = bitValue == 1 ? (byte)0xFF : (byte)0x00;
        var offset = (y * width + x) * 3;
        rgb[offset] = colorValue;
        rgb[offset + 1] = colorValue;
        rgb[offset + 2] = colorValue;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Builds a monochrome screen from a picture.</summary>
  /// <remarks>
  /// There are no colours to choose: the format shows white over black and nothing else, so this is
  /// a threshold rather than a quantisation.
  /// </remarks>
  public static HiresC64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var set = GlyphSheet.Sample(image, FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      if (!set[y * FixedWidth + x])
        continue;

      bitmap[(y / 8 * 40 + x / 8) * 8 + y % 8] |= (byte)(1 << (~x & 7));
    }

    return new() { LoadAddress = 0x2000, BitmapData = bitmap };
  }
}
