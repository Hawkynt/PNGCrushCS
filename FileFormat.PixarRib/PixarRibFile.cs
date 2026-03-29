using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PixarRib;

/// <summary>In-memory representation of a Pixar RIB texture image.</summary>
public sealed class PixarRibFile : IImageFileFormat<PixarRibFile> {

  internal const int HeaderSize = 512;


  static string IImageFileFormat<PixarRibFile>.PrimaryExtension => ".pxr";
  static string[] IImageFileFormat<PixarRibFile>.FileExtensions => [".pxr", ".pixar", ".picio"];
  static PixarRibFile IImageFileFormat<PixarRibFile>.FromFile(FileInfo file) => PixarRibReader.FromFile(file);
  static PixarRibFile IImageFileFormat<PixarRibFile>.FromBytes(byte[] data) => PixarRibReader.FromBytes(data);
  static PixarRibFile IImageFileFormat<PixarRibFile>.FromStream(Stream stream) => PixarRibReader.FromStream(stream);
  static byte[] IImageFileFormat<PixarRibFile>.ToBytes(PixarRibFile file) => PixarRibWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PixarRibFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PixarRibFile FromRawImage(RawImage image) {
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
