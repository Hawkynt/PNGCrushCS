using System;
using FileFormat.Core;

namespace FileFormat.Wsq;

/// <summary>In-memory representation of a WSQ (Wavelet Scalar Quantization) fingerprint image.</summary>
public readonly record struct WsqFile : IImageFormatReader<WsqFile>, IImageToRawImage<WsqFile>, IImageFromRawImage<WsqFile>, IImageFormatWriter<WsqFile> {

  static string IImageFormatMetadata<WsqFile>.PrimaryExtension => ".wsq";
  static string[] IImageFormatMetadata<WsqFile>.FileExtensions => [".wsq"];
  static WsqFile IImageFormatReader<WsqFile>.FromSpan(ReadOnlySpan<byte> data) => WsqReader.FromSpan(data);

  static bool? IImageFormatMetadata<WsqFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == 0xFF && header[1] == 0xA0
      ? true : null;

  static byte[] IImageFormatWriter<WsqFile>.ToBytes(WsqFile file) => WsqWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Always 8 for WSQ.</summary>
  public int BitDepth => 8;

  /// <summary>Pixels per inch (typically 500 for fingerprints).</summary>
  public int Ppi { get; init; }

  /// <summary>Compression ratio (0.0-1.0 quality, higher = better quality, lower compression).</summary>
  public double CompressionRatio { get; init; }

  /// <summary>8-bit grayscale pixel data in row-major order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(WsqFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static WsqFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
