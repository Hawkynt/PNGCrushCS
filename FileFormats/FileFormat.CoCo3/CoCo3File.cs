using System;
using FileFormat.Core;

namespace FileFormat.CoCo3;

/// <summary>In-memory representation of a CoCo 3 GIME 320x200x16 color graphics screen (38400 bytes: 4bpp, 2 pixels per byte).</summary>
public readonly record struct CoCo3File : IImageFormatReader<CoCo3File>, IImageToRawImage<CoCo3File>, IImageFromRawImage<CoCo3File>, IImageFormatWriter<CoCo3File> {

  static string IImageFormatMetadata<CoCo3File>.PrimaryExtension => ".cc3";
  static string[] IImageFormatMetadata<CoCo3File>.FileExtensions => [".cc3"];
  static CoCo3File IImageFormatReader<CoCo3File>.FromSpan(ReadOnlySpan<byte> data) => CoCo3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<CoCo3File>.ToBytes(CoCo3File file) => CoCo3Writer.ToBytes(file);

  /// <summary>Expected file size in bytes (320 * 200 / 2).</summary>
  internal const int ExpectedFileSize = 32000;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Bytes per scanline (320 / 2).</summary>
  internal const int BytesPerRow = 160;

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw pixel data (32000 bytes: 4bpp, high nybble = left pixel, low nybble = right pixel).</summary>
  public byte[] RawData { get; init; }

  /// <summary>CoCo 3 default 16-color CGA-like palette as RGB triplets.</summary>
  private static readonly byte[] _DefaultPalette = [
    0x00, 0x00, 0x00,  // 0  Black
    0x00, 0x00, 0xAA,  // 1  Blue
    0x00, 0xAA, 0x00,  // 2  Green
    0x00, 0xAA, 0xAA,  // 3  Cyan
    0xAA, 0x00, 0x00,  // 4  Red
    0xAA, 0x00, 0xAA,  // 5  Magenta
    0xAA, 0x55, 0x00,  // 6  Brown
    0xAA, 0xAA, 0xAA,  // 7  Light Gray
    0x55, 0x55, 0x55,  // 8  Dark Gray
    0x55, 0x55, 0xFF,  // 9  Light Blue
    0x55, 0xFF, 0x55,  // 10 Light Green
    0x55, 0xFF, 0xFF,  // 11 Light Cyan
    0xFF, 0x55, 0x55,  // 12 Light Red
    0xFF, 0x55, 0xFF,  // 13 Light Magenta
    0xFF, 0xFF, 0x55,  // 14 Yellow
    0xFF, 0xFF, 0xFF,  // 15 White
  ];

  /// <summary>Converts the CoCo 3 screen to an Indexed8 raw image (320x200, 16-entry palette).</summary>
  public static RawImage ToRawImage(CoCo3File file) {

    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        var b = srcOffset < file.RawData.Length ? file.RawData[srcOffset] : (byte)0;
        var dstOffset = y * PixelWidth + byteCol * 2;
        pixels[dstOffset] = (byte)((b >> 4) & 0x0F);
        if (dstOffset + 1 < pixels.Length)
          pixels[dstOffset + 1] = (byte)(b & 0x0F);
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _DefaultPalette[..],
      PaletteCount = 16,
    };
  }

  /// <summary>Creates a CoCo 3 screen from an Indexed8 raw image (320x200).</summary>
  public static CoCo3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * PixelWidth + byteCol * 2;
        var hi = image.PixelData[srcOffset] & 0x0F;
        var lo = srcOffset + 1 < image.PixelData.Length ? image.PixelData[srcOffset + 1] & 0x0F : 0;
        rawData[y * BytesPerRow + byteCol] = (byte)((hi << 4) | lo);
      }

    return new CoCo3File { RawData = rawData };
  }
}
