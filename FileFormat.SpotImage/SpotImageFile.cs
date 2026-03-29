using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SpotImage;

/// <summary>In-memory representation of a SPOT satellite imagery file.</summary>
public sealed class SpotImageFile : IImageFileFormat<SpotImageFile> {

  /// <summary>Magic bytes "SPOT".</summary>
  internal static readonly byte[] Magic = [(byte)'S', (byte)'P', (byte)'O', (byte)'T'];

  /// <summary>Header size in bytes (4 magic + 2 width + 2 height + 2 bpp + 6 reserved = 16).</summary>
  internal const int HeaderSize = 16;

  static string IImageFileFormat<SpotImageFile>.PrimaryExtension => ".dat";
  static string[] IImageFileFormat<SpotImageFile>.FileExtensions => [".dat"];
  static SpotImageFile IImageFileFormat<SpotImageFile>.FromFile(FileInfo file) => SpotImageReader.FromFile(file);
  static SpotImageFile IImageFileFormat<SpotImageFile>.FromBytes(byte[] data) => SpotImageReader.FromBytes(data);
  static SpotImageFile IImageFileFormat<SpotImageFile>.FromStream(Stream stream) => SpotImageReader.FromStream(stream);
  static RawImage IImageFileFormat<SpotImageFile>.ToRawImage(SpotImageFile file) => ToRawImage(file);
  static SpotImageFile IImageFileFormat<SpotImageFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<SpotImageFile>.ToBytes(SpotImageFile file) => SpotImageWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (8 for grayscale, 24 for RGB).</summary>
  public int BitsPerPixel { get; init; } = 8;

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts to Gray8 (8bpp) or Rgb24 (24bpp).</summary>
  public static RawImage ToRawImage(SpotImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.BitsPerPixel >= 24) {
      var expectedSize = file.Width * file.Height * 3;
      var pixelData = new byte[expectedSize];
      file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = pixelData,
      };
    }

    var graySize = file.Width * file.Height;
    var grayData = new byte[graySize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, graySize)).CopyTo(grayData);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = grayData,
    };
  }

  /// <summary>Creates a SPOT image from a Gray8 or Rgb24 raw image.</summary>
  public static SpotImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8 && image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var bpp = image.Format == PixelFormat.Rgb24 ? 24 : 8;
    var pixelData = image.PixelData[..];

    return new() { Width = image.Width, Height = image.Height, BitsPerPixel = bpp, PixelData = pixelData };
  }
}
