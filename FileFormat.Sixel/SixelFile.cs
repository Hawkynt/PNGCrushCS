using System;
using FileFormat.Core;

namespace FileFormat.Sixel;

/// <summary>In-memory representation of a Sixel (DEC terminal graphics) image.</summary>
public readonly record struct SixelFile : IImageFormatReader<SixelFile>, IImageToRawImage<SixelFile>, IImageFromRawImage<SixelFile>, IImageFormatWriter<SixelFile> {

  static string IImageFormatMetadata<SixelFile>.PrimaryExtension => ".six";
  static string[] IImageFormatMetadata<SixelFile>.FileExtensions => [".six", ".sixel"];
  static SixelFile IImageFormatReader<SixelFile>.FromSpan(ReadOnlySpan<byte> data) => SixelReader.FromSpan(data);
  static byte[] IImageFormatWriter<SixelFile>.ToBytes(SixelFile file) => SixelWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public int AspectRatio { get; init; }
  public int BackgroundMode { get; init; }

  public static RawImage ToRawImage(SixelFile file) {
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
