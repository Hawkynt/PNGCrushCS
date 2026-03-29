using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OpenRaster;

/// <summary>In-memory representation of an OpenRaster (.ora) image.</summary>
public sealed class OpenRasterFile : IImageFileFormat<OpenRasterFile> {

  static string IImageFileFormat<OpenRasterFile>.PrimaryExtension => ".ora";
  static string[] IImageFileFormat<OpenRasterFile>.FileExtensions => [".ora"];
  static OpenRasterFile IImageFileFormat<OpenRasterFile>.FromFile(FileInfo file) => OpenRasterReader.FromFile(file);
  static OpenRasterFile IImageFileFormat<OpenRasterFile>.FromBytes(byte[] data) => OpenRasterReader.FromBytes(data);
  static OpenRasterFile IImageFileFormat<OpenRasterFile>.FromStream(Stream stream) => OpenRasterReader.FromStream(stream);
  static byte[] IImageFileFormat<OpenRasterFile>.ToBytes(OpenRasterFile file) => OpenRasterWriter.ToBytes(file);
  /// <summary>Canvas width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Canvas height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Flat composite RGBA pixel data (4 bytes per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Ordered list of layers (bottom to top).</summary>
  public IReadOnlyList<OpenRasterLayer> Layers { get; init; } = [];

  public static RawImage ToRawImage(OpenRasterFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
