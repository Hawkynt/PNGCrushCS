using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.JpegXl;

/// <summary>In-memory representation of a JPEG XL image.</summary>
public sealed class JpegXlFile : IImageFileFormat<JpegXlFile> {

  static string IImageFileFormat<JpegXlFile>.PrimaryExtension => ".jxl";
  static string[] IImageFileFormat<JpegXlFile>.FileExtensions => [".jxl"];
  static JpegXlFile IImageFileFormat<JpegXlFile>.FromFile(FileInfo file) => JpegXlReader.FromFile(file);
  static JpegXlFile IImageFileFormat<JpegXlFile>.FromBytes(byte[] data) => JpegXlReader.FromBytes(data);
  static JpegXlFile IImageFileFormat<JpegXlFile>.FromStream(Stream stream) => JpegXlReader.FromStream(stream);
  static byte[] IImageFileFormat<JpegXlFile>.ToBytes(JpegXlFile file) => JpegXlWriter.ToBytes(file);

  static bool? IImageFileFormat<JpegXlFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0x0A)
      return true;
    if (header.Length >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70
        && header[8] == (byte)'j' && header[9] == (byte)'x' && header[10] == (byte)'l' && header[11] == (byte)' ')
      return true;
    return null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of color components (1 for grayscale, 3 for RGB).</summary>
  public int ComponentCount { get; init; } = 3;

  /// <summary>Raw pixel data (Gray8 or Rgb24 layout).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>ISOBMFF brand string (default "jxl ").</summary>
  public string Brand { get; init; } = "jxl ";

  public static RawImage ToRawImage(JpegXlFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.ComponentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static JpegXlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int componentCount;
    if (image.Format == PixelFormat.Gray8)
      componentCount = 1;
    else if (image.Format == PixelFormat.Rgb24)
      componentCount = 3;
    else
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
    };
  }
}
