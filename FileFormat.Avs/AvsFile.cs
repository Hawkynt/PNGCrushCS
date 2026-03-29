using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Avs;

/// <summary>In-memory representation of an AVS (Application Visualization System) image.</summary>
public sealed class AvsFile : IImageFileFormat<AvsFile> {

  static string IImageFileFormat<AvsFile>.PrimaryExtension => ".avs";
  static string[] IImageFileFormat<AvsFile>.FileExtensions => [".avs"];
  static AvsFile IImageFileFormat<AvsFile>.FromFile(FileInfo file) => AvsReader.FromFile(file);
  static AvsFile IImageFileFormat<AvsFile>.FromBytes(byte[] data) => AvsReader.FromBytes(data);
  static AvsFile IImageFileFormat<AvsFile>.FromStream(Stream stream) => AvsReader.FromStream(stream);
  static byte[] IImageFileFormat<AvsFile>.ToBytes(AvsFile file) => AvsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw ARGB pixel data (4 bytes per pixel, big-endian).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(AvsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Argb32,
      PixelData = file.PixelData[..],
    };
  }

  public static AvsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Argb32)
      throw new ArgumentException($"Expected {PixelFormat.Argb32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
