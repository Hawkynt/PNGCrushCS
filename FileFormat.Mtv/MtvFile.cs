using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mtv;

/// <summary>In-memory representation of an MTV Ray Tracer image.</summary>
public sealed class MtvFile : IImageFileFormat<MtvFile> {

  static string IImageFileFormat<MtvFile>.PrimaryExtension => ".mtv";
  static string[] IImageFileFormat<MtvFile>.FileExtensions => [".mtv"];
  static MtvFile IImageFileFormat<MtvFile>.FromFile(FileInfo file) => MtvReader.FromFile(file);
  static MtvFile IImageFileFormat<MtvFile>.FromBytes(byte[] data) => MtvReader.FromBytes(data);
  static MtvFile IImageFileFormat<MtvFile>.FromStream(Stream stream) => MtvReader.FromStream(stream);
  static byte[] IImageFileFormat<MtvFile>.ToBytes(MtvFile file) => MtvWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MtvFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MtvFile FromRawImage(RawImage image) {
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
