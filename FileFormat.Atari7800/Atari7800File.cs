using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Atari7800;

/// <summary>Atari 7800 Maria screen dump data model.</summary>
public sealed class Atari7800File : IImageFileFormat<Atari7800File> {

  public const int FileSize = 38400;
  public const int ImageWidth = 160;
  public const int ImageHeight = 240;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = new byte[12];

  public static string PrimaryExtension => ".a78";
  public static string[] FileExtensions => [".a78", ".a7800"];
  static FormatCapability IImageFileFormat<Atari7800File>.Capabilities => FormatCapability.IndexedOnly;
  public static Atari7800File FromFile(FileInfo file) => Atari7800Reader.FromFile(file);
  public static Atari7800File FromBytes(byte[] data) => Atari7800Reader.FromBytes(data);
  public static Atari7800File FromStream(Stream stream) => Atari7800Reader.FromStream(stream);
  public static byte[] ToBytes(Atari7800File file) => Atari7800Writer.ToBytes(file);

  public static RawImage ToRawImage(Atari7800File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 4,
    };
  }

  public static Atari7800File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8, got {image.Format}");
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new Atari7800File {
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : new byte[12],
    };
  }
}
