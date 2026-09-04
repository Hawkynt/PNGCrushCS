using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Analyze;

/// <summary>In-memory representation of an Analyze 7.5 medical imaging file (.hdr + .img paired files).</summary>
public readonly record struct AnalyzeFile : IImageFormatReader<AnalyzeFile>, IImageToRawImage<AnalyzeFile>, IImageFromRawImage<AnalyzeFile>, IImageFormatWriter<AnalyzeFile> {

  static string IImageFormatMetadata<AnalyzeFile>.PrimaryExtension => ".hdr";
  static string[] IImageFormatMetadata<AnalyzeFile>.FileExtensions => [".hdr", ".img"];
  static AnalyzeFile IImageFormatReader<AnalyzeFile>.FromSpan(ReadOnlySpan<byte> data) => AnalyzeReader.FromSpan(data);

  /// <summary>Reads a named file, the name being something this format needs.</summary>
  /// <remarks>
  /// Only the by-bytes entry was wired up, so the registry could never reach it.
  /// The picture is in a companion .img beside the header file, which only the reader that takes a
  /// name can find — wired through bytes alone it had nothing to decode.
  /// </remarks>
  static AnalyzeFile IImageFormatReader<AnalyzeFile>.FromFile(FileInfo file) => AnalyzeReader.FromFile(file);
  static byte[] IImageFormatWriter<AnalyzeFile>.ToBytes(AnalyzeFile file) => AnalyzeWriter.ToBytes(file);

  static bool? IImageFormatMetadata<AnalyzeFile>.MatchesSignature(ReadOnlySpan<byte> header) {
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
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AnalyzeFile file) {
    var format = file.DataType switch {
      AnalyzeDataType.UInt8 => PixelFormat.Gray8,
      AnalyzeDataType.Rgb24 => PixelFormat.Rgb24,
      _ => throw new NotSupportedException($"Unsupported Analyze data type for raw image conversion: {file.DataType}.")
    };

    // An Analyze header is legitimately voxel-less — the pair keeps them in the .img beside it — so
    // parsing one is fine and turning one into a picture is not. This used to hand back an image
    // stating its size with nothing behind it, which read as a success and then indexed off the end
    // of its own buffer on the first conversion.
    var needed = (long)file.Width * file.Height * Math.Max(file.BitsPerPixel, 1) / 8;
    if (file.PixelData is null || file.PixelData.Length < needed)
      throw new InvalidDataException(
        $"An Analyze {file.Width}x{file.Height} at {file.BitsPerPixel} bits needs {needed} bytes of voxels; "
        + $"{file.PixelData?.Length ?? 0} are present, so the .img beside this header is missing or truncated.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static AnalyzeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);
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
