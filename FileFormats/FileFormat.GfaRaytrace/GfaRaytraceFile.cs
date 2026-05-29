using System;
using FileFormat.Core;

namespace FileFormat.GfaRaytrace;

/// <summary>In-memory representation of a GfA Raytrace image image.</summary>
public readonly record struct GfaRaytraceFile : IImageFormatReader<GfaRaytraceFile>, IImageToRawImage<GfaRaytraceFile>, IImageFromRawImage<GfaRaytraceFile>, IImageFormatWriter<GfaRaytraceFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<GfaRaytraceFile>.PrimaryExtension => ".sul";
  static string[] IImageFormatMetadata<GfaRaytraceFile>.FileExtensions => [".sul"];
  static GfaRaytraceFile IImageFormatReader<GfaRaytraceFile>.FromSpan(ReadOnlySpan<byte> data) => GfaRaytraceReader.FromSpan(data);
  static byte[] IImageFormatWriter<GfaRaytraceFile>.ToBytes(GfaRaytraceFile file) => GfaRaytraceWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GfaRaytraceFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static GfaRaytraceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
