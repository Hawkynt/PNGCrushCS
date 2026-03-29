using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Sixel;

/// <summary>In-memory representation of a Sixel (DEC terminal graphics) image.</summary>
public sealed class SixelFile : IImageFileFormat<SixelFile> {

  static string IImageFileFormat<SixelFile>.PrimaryExtension => ".six";
  static string[] IImageFileFormat<SixelFile>.FileExtensions => [".six", ".sixel"];
  static SixelFile IImageFileFormat<SixelFile>.FromFile(FileInfo file) => SixelReader.FromFile(file);
  static SixelFile IImageFileFormat<SixelFile>.FromBytes(byte[] data) => SixelReader.FromBytes(data);
  static SixelFile IImageFileFormat<SixelFile>.FromStream(Stream stream) => SixelReader.FromStream(stream);
  static byte[] IImageFileFormat<SixelFile>.ToBytes(SixelFile file) => SixelWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public int AspectRatio { get; init; }
  public int BackgroundMode { get; init; }

  public static RawImage ToRawImage(SixelFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette is { } p ? p[..] : null,
      PaletteCount = file.PaletteColorCount,
    };
  }

  public static SixelFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette is { } p ? p[..] : null,
      PaletteColorCount = image.PaletteCount,
    };
  }
}
