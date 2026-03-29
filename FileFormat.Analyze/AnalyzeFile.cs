using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Analyze;

/// <summary>In-memory representation of an Analyze 7.5 medical imaging file (.hdr + .img paired files).</summary>
public sealed class AnalyzeFile : IImageFileFormat<AnalyzeFile> {

  static string IImageFileFormat<AnalyzeFile>.PrimaryExtension => ".hdr";
  static string[] IImageFileFormat<AnalyzeFile>.FileExtensions => [".hdr", ".img"];
  static AnalyzeFile IImageFileFormat<AnalyzeFile>.FromFile(FileInfo file) => AnalyzeReader.FromFile(file);
  static AnalyzeFile IImageFileFormat<AnalyzeFile>.FromBytes(byte[] data) => AnalyzeReader.FromBytes(data);
  static AnalyzeFile IImageFileFormat<AnalyzeFile>.FromStream(Stream stream) => AnalyzeReader.FromStream(stream);
  static byte[] IImageFileFormat<AnalyzeFile>.ToBytes(AnalyzeFile file) => AnalyzeWriter.ToBytes(file);

  static bool? IImageFileFormat<AnalyzeFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 44 || header[0] != 0x5C || header[1] != 0x01 || header[2] != 0x00 || header[3] != 0x00)
      return null;
    var dim0 = (short)(header[40] | (header[41] << 8));
    return dim0 >= 1 && dim0 <= 7 ? true : null;
  }

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>The Analyze 7.5 data type code.</summary>
  public AnalyzeDataType DataType { get; init; }

  /// <summary>Bits per pixel (8, 16, 24, 32).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Raw pixel data from the .img file.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(AnalyzeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var format = file.DataType switch {
      AnalyzeDataType.UInt8 => PixelFormat.Gray8,
      AnalyzeDataType.Rgb24 => PixelFormat.Rgb24,
      _ => throw new NotSupportedException($"Unsupported Analyze data type for raw image conversion: {file.DataType}.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static AnalyzeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        DataType = AnalyzeDataType.UInt8,
        BitsPerPixel = 8,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        DataType = AnalyzeDataType.Rgb24,
        BitsPerPixel = 24,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image))
    };
  }
}
