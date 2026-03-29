using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Gbr;

/// <summary>In-memory representation of a GIMP Brush (GBR) version 2 image.</summary>
[FormatMagicBytes([0x47, 0x49, 0x4D, 0x50], offset: 20)]
public sealed class GbrFile : IImageFileFormat<GbrFile> {

  static string IImageFileFormat<GbrFile>.PrimaryExtension => ".gbr";
  static string[] IImageFileFormat<GbrFile>.FileExtensions => [".gbr"];
  static GbrFile IImageFileFormat<GbrFile>.FromFile(FileInfo file) => GbrReader.FromFile(file);
  static GbrFile IImageFileFormat<GbrFile>.FromBytes(byte[] data) => GbrReader.FromBytes(data);
  static GbrFile IImageFileFormat<GbrFile>.FromStream(Stream stream) => GbrReader.FromStream(stream);
  static byte[] IImageFileFormat<GbrFile>.ToBytes(GbrFile file) => GbrWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bytes per pixel (1 = grayscale, 4 = RGBA).</summary>
  public int BytesPerPixel { get; init; }

  /// <summary>Brush spacing in percent.</summary>
  public int Spacing { get; init; }

  /// <summary>Brush name (UTF-8, stored null-terminated in file).</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>Raw pixel data (width * height * bytes_per_pixel bytes, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(GbrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.BytesPerPixel == 1 ? PixelFormat.Gray8 : PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static GbrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format is not (PixelFormat.Gray8 or PixelFormat.Rgba32))
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    var bpp = image.Format == PixelFormat.Gray8 ? 1 : 4;
    return new() {
      Width = image.Width,
      Height = image.Height,
      BytesPerPixel = bpp,
      Spacing = 10,
      Name = "Untitled",
      PixelData = image.PixelData[..],
    };
  }
}
