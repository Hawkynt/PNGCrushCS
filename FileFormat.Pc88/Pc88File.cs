using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pc88;

/// <summary>NEC PC-88 monochrome graphics screen data model.</summary>
public sealed class Pc88File : IImageFileFormat<Pc88File> {

  public const int FileSize = 16000;
  public const int ImageWidth = 640;
  public const int ImageHeight = 200;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = [0, 0, 0, 255, 255, 255];

  public static string PrimaryExtension => ".pc8";
  public static string[] FileExtensions => [".pc8"];
  public static FormatCapability Capabilities => FormatCapability.MonochromeOnly;
  public static Pc88File FromFile(FileInfo file) => Pc88Reader.FromFile(file);
  public static Pc88File FromBytes(byte[] data) => Pc88Reader.FromBytes(data);
  public static Pc88File FromStream(Stream stream) => Pc88Reader.FromStream(stream);
  public static byte[] ToBytes(Pc88File file) => Pc88Writer.ToBytes(file);

  public static RawImage ToRawImage(Pc88File file) {
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

  public static Pc88File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new Pc88File {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[6],
    };
  }
}
