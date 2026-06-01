using System;
using FileFormat.Core;

namespace FileFormat.DrHalo;

/// <summary>In-memory representation of a Dr. Halo CUT image.</summary>
public readonly record struct DrHaloFile : IImageFormatReader<DrHaloFile>, IImageToRawImage<DrHaloFile>, IImageFromRawImage<DrHaloFile>, IImageFormatWriter<DrHaloFile> {

  static string IImageFormatMetadata<DrHaloFile>.PrimaryExtension => ".cut";
  static string[] IImageFormatMetadata<DrHaloFile>.FileExtensions => [".cut"];
  static DrHaloFile IImageFormatReader<DrHaloFile>.FromSpan(ReadOnlySpan<byte> data) => DrHaloReader.FromSpan(data);
  static byte[] IImageFormatWriter<DrHaloFile>.ToBytes(DrHaloFile file) => DrHaloWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(DrHaloFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette is { } p ? p[..] : null,
      PaletteCount = file.Palette is { } pal ? pal.Length / 3 : 0,
    };
  }

  public static DrHaloFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette is { } p ? p[..] : null,
    };
  }
}
