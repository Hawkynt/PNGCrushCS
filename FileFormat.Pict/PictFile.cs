using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pict;

/// <summary>In-memory representation of a PICT image (raster subset).</summary>
public sealed class PictFile : IImageFileFormat<PictFile> {

  static string IImageFileFormat<PictFile>.PrimaryExtension => ".pict";
  static string[] IImageFileFormat<PictFile>.FileExtensions => [".pict", ".pct"];
  static PictFile IImageFileFormat<PictFile>.FromFile(FileInfo file) => PictReader.FromFile(file);
  static PictFile IImageFileFormat<PictFile>.FromBytes(byte[] data) => PictReader.FromBytes(data);
  static PictFile IImageFileFormat<PictFile>.FromStream(Stream stream) => PictReader.FromStream(stream);
  static byte[] IImageFileFormat<PictFile>.ToBytes(PictFile file) => PictWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }
  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }
  /// <summary>Bits per pixel (8 for indexed, 24 for direct RGB).</summary>
  public int BitsPerPixel { get; init; }
  /// <summary>Pixel data: RGB interleaved for 24bpp, indexed for 8bpp.</summary>
  public byte[] PixelData { get; init; } = [];
  /// <summary>Optional palette for indexed images (R,G,B triplets).</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Converts this PICT file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PictFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.BitsPerPixel == 24)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      };

    // Indexed (8bpp)
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette is { } p ? p[..] : null,
      PaletteCount = file.Palette is { } pal ? pal.Length / 3 : 0,
    };
  }

  /// <summary>Creates a <see cref="PictFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static PictFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 24,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 8,
          PixelData = image.PixelData[..],
          Palette = image.Palette is { } p ? p[..] : null,
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for PICT: {image.Format}", nameof(image));
    }
  }
}
