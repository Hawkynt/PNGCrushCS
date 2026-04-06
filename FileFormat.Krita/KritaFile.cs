using System;
using FileFormat.Core;

namespace FileFormat.Krita;

/// <summary>In-memory representation of a Krita (.kra) image.</summary>
public readonly record struct KritaFile : IImageFormatReader<KritaFile>, IImageToRawImage<KritaFile>, IImageFromRawImage<KritaFile>, IImageFormatWriter<KritaFile> {

  static string IImageFormatMetadata<KritaFile>.PrimaryExtension => ".kra";
  static string[] IImageFormatMetadata<KritaFile>.FileExtensions => [".kra"];
  static KritaFile IImageFormatReader<KritaFile>.FromSpan(ReadOnlySpan<byte> data) => KritaReader.FromSpan(data);
  static byte[] IImageFormatWriter<KritaFile>.ToBytes(KritaFile file) => KritaWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Flat composite RGBA pixel data (4 bytes per pixel, row-major).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(KritaFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static KritaFile FromRawImage(RawImage image) {
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
