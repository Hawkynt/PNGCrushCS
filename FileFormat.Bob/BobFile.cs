using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bob;

/// <summary>In-memory representation of a Bob Raytracer image image.</summary>
public sealed class BobFile : IImageFileFormat<BobFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<BobFile>.PrimaryExtension => ".bob";
  static string[] IImageFileFormat<BobFile>.FileExtensions => [".bob"];
  static BobFile IImageFileFormat<BobFile>.FromFile(FileInfo file) => BobReader.FromFile(file);
  static BobFile IImageFileFormat<BobFile>.FromBytes(byte[] data) => BobReader.FromBytes(data);
  static BobFile IImageFileFormat<BobFile>.FromStream(Stream stream) => BobReader.FromStream(stream);
  static byte[] IImageFileFormat<BobFile>.ToBytes(BobFile file) => BobWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(BobFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static BobFile FromRawImage(RawImage image) {
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
