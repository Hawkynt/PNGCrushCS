using System;
using FileFormat.Core;

namespace FileFormat.Vivid;

/// <summary>In-memory representation of a Vivid Ray-Tracer image image.</summary>
public readonly record struct VividFile : IImageFormatReader<VividFile>, IImageToRawImage<VividFile>, IImageFromRawImage<VividFile>, IImageFormatWriter<VividFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<VividFile>.PrimaryExtension => ".vivid";
  static string[] IImageFormatMetadata<VividFile>.FileExtensions => [".vivid", ".dis"];
  static VividFile IImageFormatReader<VividFile>.FromSpan(ReadOnlySpan<byte> data) => VividReader.FromSpan(data);
  static byte[] IImageFormatWriter<VividFile>.ToBytes(VividFile file) => VividWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(VividFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static VividFile FromRawImage(RawImage image) {
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
