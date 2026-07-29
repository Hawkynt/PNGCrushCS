using System;
using FileFormat.Core;

namespace FileFormat.Aai;

/// <summary>In-memory representation of an AAI (Dune HD) image.</summary>
[FormatMimeType("application/x-aai")]
public readonly record struct AaiFile : IImageFormatReader<AaiFile>, IImageToRawImage<AaiFile>, IImageFromRawImage<AaiFile>, IImageFormatWriter<AaiFile> {

  static string IImageFormatMetadata<AaiFile>.PrimaryExtension => ".aai";
  static string[] IImageFormatMetadata<AaiFile>.FileExtensions => [".aai"];
  static AaiFile IImageFormatReader<AaiFile>.FromSpan(ReadOnlySpan<byte> data) => AaiReader.FromSpan(data);
  static byte[] IImageFormatWriter<AaiFile>.ToBytes(AaiFile file) => AaiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGBA pixel data (4 bytes per pixel: R, G, B, A).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AaiFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static AaiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgba32);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
