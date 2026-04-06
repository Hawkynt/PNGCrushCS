using System;
using FileFormat.Core;

namespace FileFormat.Mtv;

/// <summary>In-memory representation of an MTV Ray Tracer image.</summary>
public readonly record struct MtvFile : IImageFormatReader<MtvFile>, IImageToRawImage<MtvFile>, IImageFromRawImage<MtvFile>, IImageFormatWriter<MtvFile> {

  static string IImageFormatMetadata<MtvFile>.PrimaryExtension => ".mtv";
  static string[] IImageFormatMetadata<MtvFile>.FileExtensions => [".mtv"];
  static MtvFile IImageFormatReader<MtvFile>.FromSpan(ReadOnlySpan<byte> data) => MtvReader.FromSpan(data);
  static byte[] IImageFormatWriter<MtvFile>.ToBytes(MtvFile file) => MtvWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MtvFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MtvFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
