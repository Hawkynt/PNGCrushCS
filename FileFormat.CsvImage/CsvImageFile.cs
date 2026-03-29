using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CsvImage;

/// <summary>In-memory representation of a CSV-encoded grayscale image.</summary>
public sealed class CsvImageFile : IImageFileFormat<CsvImageFile> {

  static string IImageFileFormat<CsvImageFile>.PrimaryExtension => ".csv";
  static string[] IImageFileFormat<CsvImageFile>.FileExtensions => [".csv"];
  static CsvImageFile IImageFileFormat<CsvImageFile>.FromFile(FileInfo file) => CsvImageReader.FromFile(file);
  static CsvImageFile IImageFileFormat<CsvImageFile>.FromBytes(byte[] data) => CsvImageReader.FromBytes(data);
  static CsvImageFile IImageFileFormat<CsvImageFile>.FromStream(Stream stream) => CsvImageReader.FromStream(stream);
  static byte[] IImageFileFormat<CsvImageFile>.ToBytes(CsvImageFile file) => CsvImageWriter.ToBytes(file);

  /// <summary>Minimum valid file size (at least header line "1,1\n").</summary>
  public const int MinFileSize = 4;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(CsvImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static CsvImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
