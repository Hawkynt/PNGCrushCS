using System;
using FileFormat.Core;

namespace FileFormat.Qtif;

/// <summary>In-memory representation of a QTIF (QuickTime Image) file.</summary>
public readonly record struct QtifFile : IImageFormatReader<QtifFile>, IImageToRawImage<QtifFile>, IImageFromRawImage<QtifFile>, IImageFormatWriter<QtifFile> {

  static string IImageFormatMetadata<QtifFile>.PrimaryExtension => ".qtif";
  static string[] IImageFormatMetadata<QtifFile>.FileExtensions => [".qtif", ".qti"];
  static QtifFile IImageFormatReader<QtifFile>.FromSpan(ReadOnlySpan<byte> data) => QtifReader.FromSpan(data);
  static byte[] IImageFormatWriter<QtifFile>.ToBytes(QtifFile file) => QtifWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QtifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static QtifFile FromRawImage(RawImage image) {
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
