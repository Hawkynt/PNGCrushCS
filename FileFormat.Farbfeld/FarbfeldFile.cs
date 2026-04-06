using System;
using FileFormat.Core;

namespace FileFormat.Farbfeld;

/// <summary>In-memory representation of a Farbfeld image.</summary>
[FormatMagicBytes([0x66, 0x61, 0x72, 0x62, 0x66, 0x65, 0x6C, 0x64])]
public readonly record struct FarbfeldFile : IImageFormatReader<FarbfeldFile>, IImageToRawImage<FarbfeldFile>, IImageFromRawImage<FarbfeldFile>, IImageFormatWriter<FarbfeldFile> {

  static string IImageFormatMetadata<FarbfeldFile>.PrimaryExtension => ".ff";
  static string[] IImageFormatMetadata<FarbfeldFile>.FileExtensions => [".ff", ".farbfeld"];
  static FarbfeldFile IImageFormatReader<FarbfeldFile>.FromSpan(ReadOnlySpan<byte> data) => FarbfeldReader.FromSpan(data);
  static byte[] IImageFormatWriter<FarbfeldFile>.ToBytes(FarbfeldFile file) => FarbfeldWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGBA16 pixel data in big-endian byte order (8 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FarbfeldFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba64,
      PixelData = file.PixelData[..],
    };
  }

  public static FarbfeldFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba64)
      throw new ArgumentException($"Expected {PixelFormat.Rgba64} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
