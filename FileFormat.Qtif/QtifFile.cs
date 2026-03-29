using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Qtif;

/// <summary>In-memory representation of a QTIF (QuickTime Image) file.</summary>
public sealed class QtifFile : IImageFileFormat<QtifFile> {

  static string IImageFileFormat<QtifFile>.PrimaryExtension => ".qtif";
  static string[] IImageFileFormat<QtifFile>.FileExtensions => [".qtif", ".qti"];
  static QtifFile IImageFileFormat<QtifFile>.FromFile(FileInfo file) => QtifReader.FromFile(file);
  static QtifFile IImageFileFormat<QtifFile>.FromBytes(byte[] data) => QtifReader.FromBytes(data);
  static QtifFile IImageFileFormat<QtifFile>.FromStream(Stream stream) => QtifReader.FromStream(stream);
  static byte[] IImageFileFormat<QtifFile>.ToBytes(QtifFile file) => QtifWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QtifFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
