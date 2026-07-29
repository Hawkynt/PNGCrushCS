using System;
using FileFormat.Core;

namespace FileFormat.Cloe;

/// <summary>In-memory representation of a Cloe Ray-Tracer image image.</summary>
public readonly record struct CloeFile : IImageFormatReader<CloeFile>, IImageToRawImage<CloeFile>, IImageFromRawImage<CloeFile>, IImageFormatWriter<CloeFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<CloeFile>.PrimaryExtension => ".clo";
  static string[] IImageFormatMetadata<CloeFile>.FileExtensions => [".clo"];
  static CloeFile IImageFormatReader<CloeFile>.FromSpan(ReadOnlySpan<byte> data) => CloeReader.FromSpan(data);
  static byte[] IImageFormatWriter<CloeFile>.ToBytes(CloeFile file) => CloeWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CloeFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CloeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
