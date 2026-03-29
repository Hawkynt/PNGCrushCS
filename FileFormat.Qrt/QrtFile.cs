using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Qrt;

/// <summary>In-memory representation of a QRT Ray Tracer image.</summary>
public sealed class QrtFile : IImageFileFormat<QrtFile> {

  static string IImageFileFormat<QrtFile>.PrimaryExtension => ".qrt";
  static string[] IImageFileFormat<QrtFile>.FileExtensions => [".qrt"];
  static QrtFile IImageFileFormat<QrtFile>.FromFile(FileInfo file) => QrtReader.FromFile(file);
  static QrtFile IImageFileFormat<QrtFile>.FromBytes(byte[] data) => QrtReader.FromBytes(data);
  static QrtFile IImageFileFormat<QrtFile>.FromStream(Stream stream) => QrtReader.FromStream(stream);
  static byte[] IImageFileFormat<QrtFile>.ToBytes(QrtFile file) => QrtWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QrtFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static QrtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
