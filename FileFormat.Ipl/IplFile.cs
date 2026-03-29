using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ipl;

/// <summary>In-memory representation of a IPL Image Sequence frame image.</summary>
public sealed class IplFile : IImageFileFormat<IplFile> {

  internal const int HeaderSize = 16;


  static string IImageFileFormat<IplFile>.PrimaryExtension => ".ipl";
  static string[] IImageFileFormat<IplFile>.FileExtensions => [".ipl"];
  static IplFile IImageFileFormat<IplFile>.FromFile(FileInfo file) => IplReader.FromFile(file);
  static IplFile IImageFileFormat<IplFile>.FromBytes(byte[] data) => IplReader.FromBytes(data);
  static IplFile IImageFileFormat<IplFile>.FromStream(Stream stream) => IplReader.FromStream(stream);
  static byte[] IImageFileFormat<IplFile>.ToBytes(IplFile file) => IplWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(IplFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static IplFile FromRawImage(RawImage image) {
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
