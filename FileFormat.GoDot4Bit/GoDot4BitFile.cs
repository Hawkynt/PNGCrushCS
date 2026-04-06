using System;
using FileFormat.Core;

namespace FileFormat.GoDot4Bit;

/// <summary>In-memory representation of a Commodore 64 GoDot 4-bit image.</summary>
public readonly record struct GoDot4BitFile : IImageFormatReader<GoDot4BitFile>, IImageToRawImage<GoDot4BitFile>, IImageFormatWriter<GoDot4BitFile> {

  static string IImageFormatMetadata<GoDot4BitFile>.PrimaryExtension => ".4bt";
  static string[] IImageFormatMetadata<GoDot4BitFile>.FileExtensions => [".4bt", ".4bit"];
  static GoDot4BitFile IImageFormatReader<GoDot4BitFile>.FromSpan(ReadOnlySpan<byte> data) => GoDot4BitReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<GoDot4BitFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<GoDot4BitFile>.ToBytes(GoDot4BitFile file) => GoDot4BitWriter.ToBytes(file);

  /// <summary>The fixed width of a GoDot 4-bit image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a GoDot 4-bit image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (160 * 200 / 2 = 16000, but padded to 16384).</summary>
  public const int ExpectedFileSize = 16384;

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

  /// <summary>Raw 4bpp packed pixel data (16384 bytes, two pixels per byte, high nibble first).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this GoDot 4-bit image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(GoDot4BitFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; x += 2) {
        var byteIndex = y * (width / 2) + x / 2;
        if (byteIndex >= file.PixelData.Length)
          break;

        var packedByte = file.PixelData[byteIndex];
        var highNibble = (packedByte >> 4) & 0x0F;
        var lowNibble = packedByte & 0x0F;

        var color0 = _C64Palette[highNibble];
        var offset0 = (y * width + x) * 3;
        rgb[offset0] = (byte)((color0 >> 16) & 0xFF);
        rgb[offset0 + 1] = (byte)((color0 >> 8) & 0xFF);
        rgb[offset0 + 2] = (byte)(color0 & 0xFF);

        if (x + 1 < width) {
          var color1 = _C64Palette[lowNibble];
          var offset1 = (y * width + x + 1) * 3;
          rgb[offset1] = (byte)((color1 >> 16) & 0xFF);
          rgb[offset1 + 1] = (byte)((color1 >> 8) & 0xFF);
          rgb[offset1 + 2] = (byte)(color1 & 0xFF);
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
