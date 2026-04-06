using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.OpenRaster;

/// <summary>In-memory representation of an OpenRaster (.ora) image.</summary>
public readonly record struct OpenRasterFile : IImageFormatReader<OpenRasterFile>, IImageToRawImage<OpenRasterFile>, IImageFromRawImage<OpenRasterFile>, IImageFormatWriter<OpenRasterFile> {

  static string IImageFormatMetadata<OpenRasterFile>.PrimaryExtension => ".ora";
  static string[] IImageFormatMetadata<OpenRasterFile>.FileExtensions => [".ora"];
  static OpenRasterFile IImageFormatReader<OpenRasterFile>.FromSpan(ReadOnlySpan<byte> data) => OpenRasterReader.FromSpan(data);
  static byte[] IImageFormatWriter<OpenRasterFile>.ToBytes(OpenRasterFile file) => OpenRasterWriter.ToBytes(file);
  /// <summary>Canvas width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Canvas height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Flat composite RGBA pixel data (4 bytes per pixel, row-major).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Ordered list of layers (bottom to top).</summary>
  public IReadOnlyList<OpenRasterLayer> Layers { get; init; }

  public static RawImage ToRawImage(OpenRasterFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static OpenRasterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
