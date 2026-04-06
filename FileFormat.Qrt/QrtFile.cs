using System;
using FileFormat.Core;

namespace FileFormat.Qrt;

/// <summary>In-memory representation of a QRT Ray Tracer image.</summary>
public readonly record struct QrtFile : IImageFormatReader<QrtFile>, IImageToRawImage<QrtFile>, IImageFromRawImage<QrtFile>, IImageFormatWriter<QrtFile> {

  static string IImageFormatMetadata<QrtFile>.PrimaryExtension => ".qrt";
  static string[] IImageFormatMetadata<QrtFile>.FileExtensions => [".qrt"];
  static QrtFile IImageFormatReader<QrtFile>.FromSpan(ReadOnlySpan<byte> data) => QrtReader.FromSpan(data);
  static byte[] IImageFormatWriter<QrtFile>.ToBytes(QrtFile file) => QrtWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QrtFile file) {
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
