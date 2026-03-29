using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Aai;

/// <summary>In-memory representation of an AAI (Dune HD) image.</summary>
public sealed class AaiFile : IImageFileFormat<AaiFile> {

  static string IImageFileFormat<AaiFile>.PrimaryExtension => ".aai";
  static string[] IImageFileFormat<AaiFile>.FileExtensions => [".aai"];
  static AaiFile IImageFileFormat<AaiFile>.FromFile(FileInfo file) => AaiReader.FromFile(file);
  static AaiFile IImageFileFormat<AaiFile>.FromBytes(byte[] data) => AaiReader.FromBytes(data);
  static AaiFile IImageFileFormat<AaiFile>.FromStream(Stream stream) => AaiReader.FromStream(stream);
  static byte[] IImageFileFormat<AaiFile>.ToBytes(AaiFile file) => AaiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGBA pixel data (4 bytes per pixel: R, G, B, A).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(AaiFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static AaiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
