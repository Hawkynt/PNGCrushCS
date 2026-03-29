using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FmTowns;

/// <summary>Fujitsu FM Towns 256-color screen dump data model.</summary>
public sealed class FmTownsFile : IImageFileFormat<FmTownsFile> {

  public const int FileSize = 64000;
  public const int ImageWidth = 320;
  public const int ImageHeight = 200;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = new byte[768];

  public static string PrimaryExtension => ".fmt";
  public static string[] FileExtensions => [".fmt"];
  static FormatCapability IImageFileFormat<FmTownsFile>.Capabilities => FormatCapability.IndexedOnly;
  public static FmTownsFile FromFile(FileInfo file) => FmTownsReader.FromFile(file);
  public static FmTownsFile FromBytes(byte[] data) => FmTownsReader.FromBytes(data);
  public static FmTownsFile FromStream(Stream stream) => FmTownsReader.FromStream(stream);
  public static byte[] ToBytes(FmTownsFile file) => FmTownsWriter.ToBytes(file);

  public static RawImage ToRawImage(FmTownsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 256,
    };
  }

  public static FmTownsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new FmTownsFile {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[768],
    };
  }
}
