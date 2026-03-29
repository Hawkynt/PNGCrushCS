using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AppleIIHgr;

/// <summary>In-memory representation of an Apple II High-Resolution graphics screen dump.</summary>
public sealed class AppleIIHgrFile : IImageFileFormat<AppleIIHgrFile> {

  /// <summary>Exact file size in bytes (8192 = 0x2000 memory region).</summary>
  internal const int FileSize = 8192;

  /// <summary>Effective pixel width (7 pixels per byte, 40 bytes per row = 280).</summary>
  internal const int PixelWidth = 280;

  /// <summary>Effective pixel height (192 scanlines).</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline row in the screen memory (40 bytes = 280/7).</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<AppleIIHgrFile>.PrimaryExtension => ".hgr";
  static string[] IImageFileFormat<AppleIIHgrFile>.FileExtensions => [".hgr"];
  static FormatCapability IImageFileFormat<AppleIIHgrFile>.Capabilities => FormatCapability.MonochromeOnly;
  static AppleIIHgrFile IImageFileFormat<AppleIIHgrFile>.FromFile(FileInfo file) => AppleIIHgrReader.FromFile(file);
  static AppleIIHgrFile IImageFileFormat<AppleIIHgrFile>.FromBytes(byte[] data) => AppleIIHgrReader.FromBytes(data);
  static AppleIIHgrFile IImageFileFormat<AppleIIHgrFile>.FromStream(Stream stream) => AppleIIHgrReader.FromStream(stream);
  static RawImage IImageFileFormat<AppleIIHgrFile>.ToRawImage(AppleIIHgrFile file) => ToRawImage(file);
  static AppleIIHgrFile IImageFileFormat<AppleIIHgrFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AppleIIHgrFile>.ToBytes(AppleIIHgrFile file) => AppleIIHgrWriter.ToBytes(file);

  /// <summary>Always 280.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw memory dump data (8192 bytes). Includes the Apple II interleaved layout and memory holes.</summary>
  public byte[] RawData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>
  /// Computes the memory offset for a given display row using the Apple II HGR interleave formula.
  /// offset = (y % 8) * 1024 + (y / 64) * 40 + ((y / 8) % 8) * 128
  /// </summary>
  internal static int GetRowOffset(int y) =>
    (y % 8) * 1024 + (y / 64) * 40 + ((y / 8) % 8) * 128;

  /// <summary>
  /// Converts the Apple II HGR screen dump to an Indexed1 raw image (280x192, B&amp;W palette).
  /// Each byte has 7 data bits (bits 0-6, LSB first) and a palette select bit (bit 7, ignored for mono).
  /// </summary>
  public static RawImage ToRawImage(AppleIIHgrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Indexed1: 280 pixels / 8 = 35 bytes per row
    var rowStride = (PixelWidth + 7) / 8; // 35
    var pixelData = new byte[rowStride * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y) {
      var srcOffset = GetRowOffset(y);
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcIndex = srcOffset + byteCol;
        var b = srcIndex < file.RawData.Length ? file.RawData[srcIndex] : (byte)0;

        // Extract 7 data bits (bits 0-6), LSB first
        for (var bit = 0; bit < 7; ++bit) {
          var px = byteCol * 7 + bit;
          if (px >= PixelWidth)
            break;

          if ((b & (1 << bit)) != 0)
            _SetPixel(pixelData, rowStride, px, y);
        }
      }
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an Apple II HGR screen dump from an Indexed1 raw image (280x192).</summary>
  public static AppleIIHgrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = (PixelWidth + 7) / 8; // 35
    var rawData = new byte[FileSize];

    for (var y = 0; y < PixelHeight; ++y) {
      var dstOffset = GetRowOffset(y);
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        byte b = 0;
        for (var bit = 0; bit < 7; ++bit) {
          var px = byteCol * 7 + bit;
          if (px >= PixelWidth)
            break;

          if (_GetPixel(image.PixelData, rowStride, px, y))
            b |= (byte)(1 << bit);
        }

        var dstIndex = dstOffset + byteCol;
        if (dstIndex < FileSize)
          rawData[dstIndex] = b;
      }
    }

    return new() { RawData = rawData };
  }

  /// <summary>Sets a single pixel in 1bpp MSB-first packed data.</summary>
  private static void _SetPixel(byte[] data, int rowStride, int x, int y) {
    var byteIndex = y * rowStride + x / 8;
    var bitIndex = 7 - (x % 8);
    data[byteIndex] |= (byte)(1 << bitIndex);
  }

  /// <summary>Gets a single pixel from 1bpp MSB-first packed data.</summary>
  private static bool _GetPixel(byte[] data, int rowStride, int x, int y) {
    var byteIndex = y * rowStride + x / 8;
    if (byteIndex >= data.Length)
      return false;

    var bitIndex = 7 - (x % 8);
    return (data[byteIndex] & (1 << bitIndex)) != 0;
  }
}
