using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Thomson;

/// <summary>Thomson TO7/MO5 binary screen dump data model.</summary>
public sealed class ThomsonFile : IImageFileFormat<ThomsonFile> {

  public const int FileSize = 8000;
  public const int ImageWidth = 320;
  public const int ImageHeight = 200;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = [0, 0, 0, 255, 255, 255];

  public static string PrimaryExtension => ".map";
  public static string[] FileExtensions => [".map"];
  public static FormatCapability Capabilities => FormatCapability.MonochromeOnly;
  public static ThomsonFile FromFile(FileInfo file) => ThomsonReader.FromFile(file);
  public static ThomsonFile FromBytes(byte[] data) => ThomsonReader.FromBytes(data);
  public static ThomsonFile FromStream(Stream stream) => ThomsonReader.FromStream(stream);
  public static byte[] ToBytes(ThomsonFile file) => ThomsonWriter.ToBytes(file);

  public static RawImage ToRawImage(ThomsonFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 2,
    };
  }

  public static ThomsonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new ThomsonFile {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[6],
    };
  }
}
