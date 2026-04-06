using System;
using FileFormat.Core;

namespace FileFormat.MsxFont;

/// <summary>In-memory representation of an MSX font pattern table (2048 bytes: 256 characters x 8 bytes each, 8x8 mono).</summary>
public readonly record struct MsxFontFile : IImageFormatReader<MsxFontFile>, IImageToRawImage<MsxFontFile>, IImageFormatWriter<MsxFontFile> {

  static string IImageFormatMetadata<MsxFontFile>.PrimaryExtension => ".fnt";
  static string[] IImageFormatMetadata<MsxFontFile>.FileExtensions => [".fnt", ".mft"];
  static MsxFontFile IImageFormatReader<MsxFontFile>.FromSpan(ReadOnlySpan<byte> data) => MsxFontReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MsxFontFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<MsxFontFile>.ToBytes(MsxFontFile file) => MsxFontWriter.ToBytes(file);

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
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the MSX font table to an Indexed1 raw image (128x128, B&amp;W palette).</summary>
  public static RawImage ToRawImage(MsxFontFile file) {

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

}
