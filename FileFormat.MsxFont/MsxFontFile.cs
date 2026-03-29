using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxFont;

/// <summary>In-memory representation of an MSX font pattern table (2048 bytes: 256 characters x 8 bytes each, 8x8 mono).</summary>
public sealed class MsxFontFile : IImageFileFormat<MsxFontFile> {

  static string IImageFileFormat<MsxFontFile>.PrimaryExtension => ".fnt";
  static string[] IImageFileFormat<MsxFontFile>.FileExtensions => [".fnt", ".mft"];
  static FormatCapability IImageFileFormat<MsxFontFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MsxFontFile IImageFileFormat<MsxFontFile>.FromFile(FileInfo file) => MsxFontReader.FromFile(file);
  static MsxFontFile IImageFileFormat<MsxFontFile>.FromBytes(byte[] data) => MsxFontReader.FromBytes(data);
  static MsxFontFile IImageFileFormat<MsxFontFile>.FromStream(Stream stream) => MsxFontReader.FromStream(stream);
  static RawImage IImageFileFormat<MsxFontFile>.ToRawImage(MsxFontFile file) => ToRawImage(file);
  static MsxFontFile IImageFileFormat<MsxFontFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<MsxFontFile>.ToBytes(MsxFontFile file) => MsxFontWriter.ToBytes(file);

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = 2048;

  /// <summary>Number of characters in the font.</summary>
  internal const int CharCount = 256;

  /// <summary>Bytes per character (8x8 mono = 8 bytes).</summary>
  internal const int BytesPerChar = 8;

  /// <summary>Character width in pixels.</summary>
  internal const int CharWidth = 8;

  /// <summary>Character height in pixels.</summary>
  internal const int CharHeight = 8;

  /// <summary>Characters per row in the rendered grid.</summary>
  internal const int CharsPerRow = 16;

  /// <summary>Number of rows in the rendered grid.</summary>
  internal const int GridRows = 16;

  /// <summary>Output image width: 16 chars x 8 pixels = 128.</summary>
  internal const int PixelWidth = 128;

  /// <summary>Output image height: 16 rows x 8 pixels = 128.</summary>
  internal const int PixelHeight = 128;

  /// <summary>Always 128.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 128.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw font pattern data (2048 bytes).</summary>
  public byte[] RawData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the MSX font table to an Indexed1 raw image (128x128, B&amp;W palette).</summary>
  public static RawImage ToRawImage(MsxFontFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = PixelWidth / 8;
    var pixelData = new byte[rowStride * PixelHeight];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % CharsPerRow;
      var gridRow = charIndex / CharsPerRow;
      var baseX = gridCol * CharWidth;
      var baseY = gridRow * CharHeight;

      for (var row = 0; row < CharHeight; ++row) {
        var dataOffset = charIndex * BytesPerChar + row;
        var charByte = dataOffset < file.RawData.Length ? file.RawData[dataOffset] : (byte)0;

        for (var bit = 0; bit < CharWidth; ++bit) {
          if (((charByte >> (7 - bit)) & 1) == 0)
            continue;

          var px = baseX + bit;
          var py = baseY + row;
          var byteIndex = py * rowStride + px / 8;
          var bitIndex = 7 - (px % 8);
          pixelData[byteIndex] |= (byte)(1 << bitIndex);
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

  /// <summary>Not supported. MSX font tables have fixed structure constraints.</summary>
  public static MsxFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MsxFontFile is not supported.");
  }
}
