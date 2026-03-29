using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GfaRaytrace;

/// <summary>In-memory representation of a GfA Raytrace image image.</summary>
public sealed class GfaRaytraceFile : IImageFileFormat<GfaRaytraceFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<GfaRaytraceFile>.PrimaryExtension => ".sul";
  static string[] IImageFileFormat<GfaRaytraceFile>.FileExtensions => [".sul"];
  static GfaRaytraceFile IImageFileFormat<GfaRaytraceFile>.FromFile(FileInfo file) => GfaRaytraceReader.FromFile(file);
  static GfaRaytraceFile IImageFileFormat<GfaRaytraceFile>.FromBytes(byte[] data) => GfaRaytraceReader.FromBytes(data);
  static GfaRaytraceFile IImageFileFormat<GfaRaytraceFile>.FromStream(Stream stream) => GfaRaytraceReader.FromStream(stream);
  static byte[] IImageFileFormat<GfaRaytraceFile>.ToBytes(GfaRaytraceFile file) => GfaRaytraceWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(GfaRaytraceFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
