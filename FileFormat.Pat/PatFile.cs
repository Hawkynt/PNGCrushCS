using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pat;

/// <summary>In-memory representation of a GIMP Pattern (PAT) image.</summary>
[FormatMagicBytes([0x47, 0x50, 0x41, 0x54], offset: 20)]
public sealed class PatFile : IImageFileFormat<PatFile> {

  static string IImageFileFormat<PatFile>.PrimaryExtension => ".pat";
  static string[] IImageFileFormat<PatFile>.FileExtensions => [".pat"];
  static PatFile IImageFileFormat<PatFile>.FromFile(FileInfo file) => PatReader.FromFile(file);
  static PatFile IImageFileFormat<PatFile>.FromBytes(byte[] data) => PatReader.FromBytes(data);
  static PatFile IImageFileFormat<PatFile>.FromStream(Stream stream) => PatReader.FromStream(stream);
  static byte[] IImageFileFormat<PatFile>.ToBytes(PatFile file) => PatWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bytes per pixel (1=grayscale, 2=gray+alpha, 3=RGB, 4=RGBA).</summary>
  public int BytesPerPixel { get; init; }

  /// <summary>Pattern name (null-terminated UTF-8 string from the header).</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>Raw pixel data (width * height * bytes_per_pixel bytes, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PatFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.BytesPerPixel switch {
        1 => PixelFormat.Gray8,
        2 => PixelFormat.GrayAlpha16,
        3 => PixelFormat.Rgb24,
        4 => PixelFormat.Rgba32,
        _ => throw new InvalidDataException($"Unsupported bytes per pixel: {file.BytesPerPixel}.")
      },
      PixelData = file.PixelData[..],
    };
  }

  public static PatFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var bpp = image.Format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.GrayAlpha16 => 2,
      PixelFormat.Rgb24 => 3,
      PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentException($"Unsupported pixel format for PAT: {image.Format}.", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      BytesPerPixel = bpp,
      Name = "Untitled",
      PixelData = image.PixelData[..],
    };
  }
}
