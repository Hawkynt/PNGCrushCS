using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DrHalo;

/// <summary>In-memory representation of a Dr. Halo CUT image.</summary>
public sealed class DrHaloFile : IImageFileFormat<DrHaloFile> {

  static string IImageFileFormat<DrHaloFile>.PrimaryExtension => ".cut";
  static string[] IImageFileFormat<DrHaloFile>.FileExtensions => [".cut"];
  static DrHaloFile IImageFileFormat<DrHaloFile>.FromFile(FileInfo file) => DrHaloReader.FromFile(file);
  static DrHaloFile IImageFileFormat<DrHaloFile>.FromBytes(byte[] data) => DrHaloReader.FromBytes(data);
  static DrHaloFile IImageFileFormat<DrHaloFile>.FromStream(Stream stream) => DrHaloReader.FromStream(stream);
  static byte[] IImageFileFormat<DrHaloFile>.ToBytes(DrHaloFile file) => DrHaloWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(DrHaloFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
