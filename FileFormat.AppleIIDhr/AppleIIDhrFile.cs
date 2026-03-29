using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AppleIIDhr;

/// <summary>In-memory representation of an Apple II Double High-Resolution graphics screen dump.</summary>
public sealed class AppleIIDhrFile : IImageFileFormat<AppleIIDhrFile> {

  /// <summary>Exact file size in bytes (16384 = two banks of 8192 each: aux + main).</summary>
  internal const int FileSize = 16384;

  /// <summary>Size of one memory bank (8192 bytes).</summary>
  internal const int BankSize = 8192;

  /// <summary>Effective pixel width (7 pixels per byte, 80 bytes per row, interleaved = 560).</summary>
  internal const int PixelWidth = 560;

  /// <summary>Effective pixel height (192 scanlines).</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline row per bank (40 bytes).</summary>
  internal const int BytesPerRowPerBank = 40;

  static string IImageFileFormat<AppleIIDhrFile>.PrimaryExtension => ".dhr";
  static string[] IImageFileFormat<AppleIIDhrFile>.FileExtensions => [".dhr", ".a2d"];
  static FormatCapability IImageFileFormat<AppleIIDhrFile>.Capabilities => FormatCapability.MonochromeOnly;
  static AppleIIDhrFile IImageFileFormat<AppleIIDhrFile>.FromFile(FileInfo file) => AppleIIDhrReader.FromFile(file);
  static AppleIIDhrFile IImageFileFormat<AppleIIDhrFile>.FromBytes(byte[] data) => AppleIIDhrReader.FromBytes(data);
  static AppleIIDhrFile IImageFileFormat<AppleIIDhrFile>.FromStream(Stream stream) => AppleIIDhrReader.FromStream(stream);
  static RawImage IImageFileFormat<AppleIIDhrFile>.ToRawImage(AppleIIDhrFile file) => ToRawImage(file);
  static AppleIIDhrFile IImageFileFormat<AppleIIDhrFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AppleIIDhrFile>.ToBytes(AppleIIDhrFile file) => AppleIIDhrWriter.ToBytes(file);

  /// <summary>Always 560.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw memory dump data (16384 bytes). First 8192 = aux bank, next 8192 = main bank.</summary>
  public byte[] RawData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>
  /// Computes the memory offset within a single bank for a given display row using the Apple II HGR interleave formula.
  /// offset = (y % 8) * 1024 + (y / 64) * 40 + ((y / 8) % 8) * 128
  /// </summary>
  internal static int GetRowOffset(int y) =>
    (y % 8) * 1024 + (y / 64) * 40 + ((y / 8) % 8) * 128;

  /// <summary>
  /// Converts the Apple II DHGR screen dump to an Indexed1 raw image (560x192, B&amp;W palette).
  /// Aux bank provides even byte columns, main bank provides odd byte columns.
  /// Each byte has 7 data bits (bits 0-6, LSB first).
  /// </summary>
  public static RawImage ToRawImage(AppleIIDhrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = (PixelWidth + 7) / 8; // 70
    var pixelData = new byte[rowStride * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y) {
      var bankOffset = GetRowOffset(y);

      // Process 80 byte columns: aux[0], main[0], aux[1], main[1], ...
      for (var col = 0; col < BytesPerRowPerBank; ++col) {
        // Aux byte (even byte column)
        var auxIndex = bankOffset + col;
        var auxByte = auxIndex < BankSize && auxIndex < file.RawData.Length ? file.RawData[auxIndex] : (byte)0;

        // Main byte (odd byte column)
        var mainIndex = BankSize + bankOffset + col;
        var mainByte = mainIndex < FileSize && mainIndex < file.RawData.Length ? file.RawData[mainIndex] : (byte)0;

        // Aux provides pixels at positions col*14 + 0..6
        for (var bit = 0; bit < 7; ++bit) {
          var px = col * 14 + bit;
          if (px >= PixelWidth)
            break;
          if ((auxByte & (1 << bit)) != 0)
            _SetPixel(pixelData, rowStride, px, y);
        }

        // Main provides pixels at positions col*14 + 7..13
        for (var bit = 0; bit < 7; ++bit) {
          var px = col * 14 + 7 + bit;
          if (px >= PixelWidth)
            break;
          if ((mainByte & (1 << bit)) != 0)
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

  /// <summary>Creates an Apple II DHGR screen dump from an Indexed1 raw image (560x192).</summary>
  public static AppleIIDhrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = (PixelWidth + 7) / 8; // 70
    var rawData = new byte[FileSize];

    for (var y = 0; y < PixelHeight; ++y) {
      var bankOffset = GetRowOffset(y);

      for (var col = 0; col < BytesPerRowPerBank; ++col) {
        // Aux byte: pixels at col*14 + 0..6
        byte auxByte = 0;
        for (var bit = 0; bit < 7; ++bit) {
          var px = col * 14 + bit;
          if (px >= PixelWidth)
            break;
          if (_GetPixel(image.PixelData, rowStride, px, y))
            auxByte |= (byte)(1 << bit);
        }

        var auxIndex = bankOffset + col;
        if (auxIndex < BankSize)
          rawData[auxIndex] = auxByte;

        // Main byte: pixels at col*14 + 7..13
        byte mainByte = 0;
        for (var bit = 0; bit < 7; ++bit) {
          var px = col * 14 + 7 + bit;
          if (px >= PixelWidth)
            break;
          if (_GetPixel(image.PixelData, rowStride, px, y))
            mainByte |= (byte)(1 << bit);
        }

        var mainIndex = BankSize + bankOffset + col;
        if (mainIndex < FileSize)
          rawData[mainIndex] = mainByte;
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
