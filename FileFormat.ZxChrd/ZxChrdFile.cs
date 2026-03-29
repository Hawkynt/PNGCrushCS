using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxChrd;

/// <summary>In-memory representation of a ZX Spectrum character set file (2048 bytes: 256 characters x 8 bytes each, 1bpp 8x8 per character).</summary>
public sealed class ZxChrdFile : IImageFileFormat<ZxChrdFile> {

  static string IImageFileFormat<ZxChrdFile>.PrimaryExtension => ".chr";
  static string[] IImageFileFormat<ZxChrdFile>.FileExtensions => [".chr", ".chrd"];
  static FormatCapability IImageFileFormat<ZxChrdFile>.Capabilities => FormatCapability.MonochromeOnly;
  static ZxChrdFile IImageFileFormat<ZxChrdFile>.FromFile(FileInfo file) => ZxChrdReader.FromFile(file);
  static ZxChrdFile IImageFileFormat<ZxChrdFile>.FromBytes(byte[] data) => ZxChrdReader.FromBytes(data);
  static ZxChrdFile IImageFileFormat<ZxChrdFile>.FromStream(Stream stream) => ZxChrdReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxChrdFile>.ToBytes(ZxChrdFile file) => ZxChrdWriter.ToBytes(file);

  /// <summary>Number of characters in the font.</summary>
  public const int CharCount = 256;

  /// <summary>Bytes per character (8 rows of 1 byte each).</summary>
  public const int BytesPerChar = 8;

  /// <summary>Characters per row when rendered as a grid.</summary>
  public const int CharsPerRow = 16;

  /// <summary>Number of character rows in the grid.</summary>
  public const int CharRows = 16;

  /// <summary>Output image width: 16 chars x 8 pixels = 128.</summary>
  public int Width => 128;

  /// <summary>Output image height: 16 rows x 8 pixels = 128.</summary>
  public int Height => 128;

  /// <summary>2048 bytes of character data (256 characters x 8 bytes).</summary>
  public byte[] CharacterData { get; init; } = [];

  /// <summary>Converts this character set to Indexed1 rendered as a 128x128 grid (16x16 characters).</summary>
  public static RawImage ToRawImage(ZxChrdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 128;
    const int height = 128;
    var bytesPerRow = width / 8; // 16 bytes per row
    var pixelData = new byte[bytesPerRow * height];

    for (var charIndex = 0; charIndex < CharCount; ++charIndex) {
      var gridCol = charIndex % CharsPerRow;
      var gridRow = charIndex / CharsPerRow;

      for (var line = 0; line < 8; ++line) {
        var charByte = file.CharacterData[charIndex * BytesPerChar + line];
        var y = gridRow * 8 + line;
        var byteOffset = y * bytesPerRow + gridCol;
        pixelData[byteOffset] = charByte;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  /// <summary>Not supported.</summary>
  public static ZxChrdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxChrdFile is not supported.");
  }
}
