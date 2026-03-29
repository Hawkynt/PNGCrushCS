using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vivid;

/// <summary>In-memory representation of a Vivid Ray-Tracer image image.</summary>
public sealed class VividFile : IImageFileFormat<VividFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<VividFile>.PrimaryExtension => ".vivid";
  static string[] IImageFileFormat<VividFile>.FileExtensions => [".vivid", ".dis"];
  static VividFile IImageFileFormat<VividFile>.FromFile(FileInfo file) => VividReader.FromFile(file);
  static VividFile IImageFileFormat<VividFile>.FromBytes(byte[] data) => VividReader.FromBytes(data);
  static VividFile IImageFileFormat<VividFile>.FromStream(Stream stream) => VividReader.FromStream(stream);
  static byte[] IImageFileFormat<VividFile>.ToBytes(VividFile file) => VividWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(VividFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static VividFile FromRawImage(RawImage image) {
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
