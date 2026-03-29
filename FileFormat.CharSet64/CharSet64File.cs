using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CharSet64;

/// <summary>In-memory representation of a C64 character set (256 chars x 8 bytes each = 2048 bytes).</summary>
public sealed class CharSet64File : IImageFileFormat<CharSet64File> {

  /// <summary>Number of characters in the set.</summary>
  internal const int CharCount = 256;

  /// <summary>Bytes per character (8x8 pixels, 1bpp = 8 bytes).</summary>
  internal const int BytesPerChar = 8;

  /// <summary>Expected file size: 256 * 8 = 2048.</summary>
  public const int ExpectedFileSize = CharCount * BytesPerChar;

  /// <summary>Character width in pixels.</summary>
  internal const int CharWidth = 8;

  /// <summary>Character height in pixels.</summary>
  internal const int CharHeight = 8;

  /// <summary>Number of characters per grid row (16x16 grid).</summary>
  internal const int GridColumns = 16;

  /// <summary>Number of characters per grid column.</summary>
  internal const int GridRows = 16;

  /// <summary>Output image width in pixels (16 * 8 = 128).</summary>
  internal const int PixelWidth = GridColumns * CharWidth;

  /// <summary>Output image height in pixels (16 * 8 = 128).</summary>
  internal const int PixelHeight = GridRows * CharHeight;

  /// <summary>Black and white palette for indexed output (2 entries, 3 bytes each).</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<CharSet64File>.PrimaryExtension => ".chr64";
  static string[] IImageFileFormat<CharSet64File>.FileExtensions => [".chr64"];
  static FormatCapability IImageFileFormat<CharSet64File>.Capabilities => FormatCapability.MonochromeOnly;
  static CharSet64File IImageFileFormat<CharSet64File>.FromFile(FileInfo file) => CharSet64Reader.FromFile(file);
  static CharSet64File IImageFileFormat<CharSet64File>.FromBytes(byte[] data) => CharSet64Reader.FromBytes(data);
  static CharSet64File IImageFileFormat<CharSet64File>.FromStream(Stream stream) => CharSet64Reader.FromStream(stream);
  static RawImage IImageFileFormat<CharSet64File>.ToRawImage(CharSet64File file) => ToRawImage(file);
  static CharSet64File IImageFileFormat<CharSet64File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<CharSet64File>.ToBytes(CharSet64File file) => CharSet64Writer.ToBytes(file);

  /// <summary>Always 128 (16 chars x 8 pixels).</summary>
  public int Width => PixelWidth;

  /// <summary>Always 128 (16 chars x 8 pixels).</summary>
  public int Height => PixelHeight;

  /// <summary>Raw character set data (2048 bytes: 256 characters x 8 bytes each).</summary>
  public byte[] CharData { get; init; } = [];

  /// <summary>
  /// Converts this C64 character set to a platform-independent <see cref="RawImage"/> in Indexed1 format.
  /// Characters are arranged in a 16x16 grid, each 8x8 pixels, producing a 128x128 image.
  /// </summary>
  public static RawImage ToRawImage(CharSet64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = PixelWidth / 8; // 16 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % GridColumns;
      var gridRow = charIndex / GridColumns;
      var baseX = gridCol * CharWidth;
      var baseY = gridRow * CharHeight;

      for (var cy = 0; cy < CharHeight; ++cy) {
        var dataOffset = charIndex * BytesPerChar + cy;
        var charByte = dataOffset < file.CharData.Length ? file.CharData[dataOffset] : (byte)0;

        for (var cx = 0; cx < CharWidth; ++cx) {
          var bitValue = (charByte >> (7 - cx)) & 1;
          if (bitValue != 0)
            _SetPixel(pixelData, rowStride, baseX + cx, baseY + cy);
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

  /// <summary>Creates a C64 character set from an Indexed1 128x128 raw image.</summary>
  public static CharSet64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = PixelWidth / 8; // 16 bytes per row
    var charData = new byte[ExpectedFileSize];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % GridColumns;
      var gridRow = charIndex / GridColumns;
      var baseX = gridCol * CharWidth;
      var baseY = gridRow * CharHeight;

      for (var cy = 0; cy < CharHeight; ++cy) {
        byte charByte = 0;
        for (var cx = 0; cx < CharWidth; ++cx)
          if (_GetPixel(image.PixelData, rowStride, baseX + cx, baseY + cy))
            charByte |= (byte)(1 << (7 - cx));

        charData[charIndex * BytesPerChar + cy] = charByte;
      }
    }

    return new() { CharData = charData };
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
