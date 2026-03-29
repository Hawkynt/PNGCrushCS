using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cloe;

/// <summary>In-memory representation of a Cloe Ray-Tracer image image.</summary>
public sealed class CloeFile : IImageFileFormat<CloeFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<CloeFile>.PrimaryExtension => ".clo";
  static string[] IImageFileFormat<CloeFile>.FileExtensions => [".clo"];
  static CloeFile IImageFileFormat<CloeFile>.FromFile(FileInfo file) => CloeReader.FromFile(file);
  static CloeFile IImageFileFormat<CloeFile>.FromBytes(byte[] data) => CloeReader.FromBytes(data);
  static CloeFile IImageFileFormat<CloeFile>.FromStream(Stream stream) => CloeReader.FromStream(stream);
  static byte[] IImageFileFormat<CloeFile>.ToBytes(CloeFile file) => CloeWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(CloeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CloeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
