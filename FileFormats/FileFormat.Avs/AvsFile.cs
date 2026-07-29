using System;
using FileFormat.Core;

namespace FileFormat.Avs;

/// <summary>In-memory representation of an AVS (Application Visualization System) image.</summary>
public readonly record struct AvsFile : IImageFormatReader<AvsFile>, IImageToRawImage<AvsFile>, IImageFromRawImage<AvsFile>, IImageFormatWriter<AvsFile> {

  static string IImageFormatMetadata<AvsFile>.PrimaryExtension => ".avs";
  static string[] IImageFormatMetadata<AvsFile>.FileExtensions => [".avs"];
  static AvsFile IImageFormatReader<AvsFile>.FromSpan(ReadOnlySpan<byte> data) => AvsReader.FromSpan(data);
  static byte[] IImageFormatWriter<AvsFile>.ToBytes(AvsFile file) => AvsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw ARGB pixel data (4 bytes per pixel, big-endian).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AvsFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Argb32,
      PixelData = file.PixelData[..],
    };
  }

  public static AvsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Argb32);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
