using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariFont;

/// <summary>In-memory representation of an Atari 8-bit character set. 128 chars arranged in a 128x64 grid (16x8 chars, 8x8 each).</summary>
public sealed class AtariFontFile : IImageFileFormat<AtariFontFile> {

  /// <summary>Number of characters in the set.</summary>
  internal const int CharCount = 128;

  /// <summary>Width of each character in pixels.</summary>
  internal const int CharWidth = 8;

  /// <summary>Height of each character in pixels.</summary>
  internal const int CharHeight = 8;

  /// <summary>Bytes per character (8 bytes of 1bpp data).</summary>
  internal const int BytesPerChar = CharHeight;

  /// <summary>Number of character columns in the output grid.</summary>
  internal const int GridColumns = 16;

  /// <summary>Number of character rows in the output grid.</summary>
  internal const int GridRows = 8;

  /// <summary>Image width in pixels (16 chars x 8 pixels).</summary>
  internal const int PixelWidth = GridColumns * CharWidth;

  /// <summary>Image height in pixels (8 chars x 8 pixels).</summary>
  internal const int PixelHeight = GridRows * CharHeight;

  /// <summary>Exact file size in bytes (128 chars x 8 bytes each).</summary>
  internal const int FileSize = CharCount * BytesPerChar;

  static string IImageFileFormat<AtariFontFile>.PrimaryExtension => ".fnt8";
  static string[] IImageFileFormat<AtariFontFile>.FileExtensions => [".fnt8"];
  static FormatCapability IImageFileFormat<AtariFontFile>.Capabilities => FormatCapability.MonochromeOnly;
  static AtariFontFile IImageFileFormat<AtariFontFile>.FromFile(FileInfo file) => AtariFontReader.FromFile(file);
  static AtariFontFile IImageFileFormat<AtariFontFile>.FromBytes(byte[] data) => AtariFontReader.FromBytes(data);
  static AtariFontFile IImageFileFormat<AtariFontFile>.FromStream(Stream stream) => AtariFontReader.FromStream(stream);
  static RawImage IImageFileFormat<AtariFontFile>.ToRawImage(AtariFontFile file) => ToRawImage(file);
  static AtariFontFile IImageFileFormat<AtariFontFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AtariFontFile>.ToBytes(AtariFontFile file) => AtariFontWriter.ToBytes(file);

  /// <summary>Always 128.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 64.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw character set data (1024 bytes: 128 chars x 8 bytes each). Each byte is one row of 8 pixels, MSB-first.</summary>
  public byte[] FontData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the character set to an Indexed1 raw image (128x64, B&amp;W palette). Characters arranged in a 16x8 grid.</summary>
  public static RawImage ToRawImage(AtariFontFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = PixelWidth / 8; // 16 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % GridColumns;
      var gridRow = charIndex / GridColumns;
      var charBaseX = gridCol * CharWidth;
      var charBaseY = gridRow * CharHeight;

      for (var row = 0; row < CharHeight; ++row) {
        var srcIndex = charIndex * BytesPerChar + row;
        var charByte = srcIndex < file.FontData.Length ? file.FontData[srcIndex] : (byte)0;

        // Write the 8 pixels of this character row into the image
        var pixelY = charBaseY + row;
        var byteOffset = pixelY * rowStride + charBaseX / 8;

        // Since CharWidth=8 and charBaseX is always byte-aligned (multiples of 8), we can copy directly
        pixelData[byteOffset] = charByte;
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

  /// <summary>Creates a character set from an Indexed1 raw image (128x64). Characters extracted from 16x8 grid.</summary>
  public static AtariFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = PixelWidth / 8;
    var fontData = new byte[FileSize];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % GridColumns;
      var gridRow = charIndex / GridColumns;
      var charBaseX = gridCol * CharWidth;
      var charBaseY = gridRow * CharHeight;

      for (var row = 0; row < CharHeight; ++row) {
        var pixelY = charBaseY + row;
        var byteOffset = pixelY * rowStride + charBaseX / 8;
        fontData[charIndex * BytesPerChar + row] = image.PixelData[byteOffset];
      }
    }

    return new() { FontData = fontData };
  }
}
