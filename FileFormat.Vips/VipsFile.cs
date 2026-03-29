using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vips;

/// <summary>In-memory representation of a libvips native image (.v / .vips).</summary>
public sealed class VipsFile : IImageFileFormat<VipsFile> {

  static string IImageFileFormat<VipsFile>.PrimaryExtension => ".v";
  static string[] IImageFileFormat<VipsFile>.FileExtensions => [".v", ".vips"];
  static VipsFile IImageFileFormat<VipsFile>.FromFile(FileInfo file) => VipsReader.FromFile(file);
  static VipsFile IImageFileFormat<VipsFile>.FromBytes(byte[] data) => VipsReader.FromBytes(data);
  static VipsFile IImageFileFormat<VipsFile>.FromStream(Stream stream) => VipsReader.FromStream(stream);
  static byte[] IImageFileFormat<VipsFile>.ToBytes(VipsFile file) => VipsWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of bands/channels (1=gray, 3=RGB, 4=RGBA).</summary>
  public int Bands { get; init; } = 3;

  /// <summary>Band sample format.</summary>
  public VipsBandFormat BandFormat { get; init; }

  /// <summary>Raw pixel data (Bands bytes per pixel for UChar).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(VipsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.BandFormat != VipsBandFormat.UChar)
      throw new NotSupportedException($"Only UChar band format is supported, got {file.BandFormat}.");

    return file.Bands switch {
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      },
      3 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = PixelConverter.Rgba32ToRgb24(file.PixelData, file.Width * file.Height),
      },
      _ => throw new NotSupportedException($"Unsupported band count: {file.Bands}."),
    };
  }

  public static VipsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        Bands = 1,
        BandFormat = VipsBandFormat.UChar,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        Bands = 3,
        BandFormat = VipsBandFormat.UChar,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image)),
    };
  }

}
