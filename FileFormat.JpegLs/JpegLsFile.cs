using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.JpegLs;

/// <summary>In-memory representation of a JPEG-LS (ITU-T T.87) image.</summary>
[FormatDetectionPriority(10)]
public sealed class JpegLsFile : IImageFileFormat<JpegLsFile> {

  static string IImageFileFormat<JpegLsFile>.PrimaryExtension => ".jls";
  static string[] IImageFileFormat<JpegLsFile>.FileExtensions => [".jls"];

  static bool? IImageFileFormat<JpegLsFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF && header[3] == 0xF7
      ? true : null;

  static JpegLsFile IImageFileFormat<JpegLsFile>.FromFile(FileInfo file) => JpegLsReader.FromFile(file);
  static JpegLsFile IImageFileFormat<JpegLsFile>.FromBytes(byte[] data) => JpegLsReader.FromBytes(data);
  static JpegLsFile IImageFileFormat<JpegLsFile>.FromStream(Stream stream) => JpegLsReader.FromStream(stream);
  static byte[] IImageFileFormat<JpegLsFile>.ToBytes(JpegLsFile file) => JpegLsWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per sample (8 or 16).</summary>
  public int BitsPerSample { get; init; } = 8;

  /// <summary>Number of components (1 for grayscale, 3 for RGB).</summary>
  public int ComponentCount { get; init; } = 1;

  /// <summary>Near-lossless parameter (0 = lossless).</summary>
  public int NearLossless { get; init; }

  /// <summary>Pixel data: for 8-bit: Gray8 (1 comp) or Rgb24 (3 comp); for 16-bit: Gray16 BE (1 comp) or Rgb48 BE (3 comp).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(JpegLsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    PixelFormat format;
    if (file.BitsPerSample > 8)
      format = file.ComponentCount == 1 ? PixelFormat.Gray16 : PixelFormat.Rgb48;
    else
      format = file.ComponentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static JpegLsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int componentCount;
    int bitsPerSample;
    switch (image.Format) {
      case PixelFormat.Gray8:
        componentCount = 1;
        bitsPerSample = 8;
        break;
      case PixelFormat.Rgb24:
        componentCount = 3;
        bitsPerSample = 8;
        break;
      case PixelFormat.Gray16:
        componentCount = 1;
        bitsPerSample = 16;
        break;
      case PixelFormat.Rgb48:
        componentCount = 3;
        bitsPerSample = 16;
        break;
      default:
        throw new ArgumentException($"Expected {PixelFormat.Gray8}, {PixelFormat.Rgb24}, {PixelFormat.Gray16}, or {PixelFormat.Rgb48} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerSample = bitsPerSample,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
    };
  }
}
