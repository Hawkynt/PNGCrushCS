using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Wsq;

/// <summary>In-memory representation of a WSQ (Wavelet Scalar Quantization) fingerprint image.</summary>
public sealed class WsqFile : IImageFileFormat<WsqFile> {

  static string IImageFileFormat<WsqFile>.PrimaryExtension => ".wsq";
  static string[] IImageFileFormat<WsqFile>.FileExtensions => [".wsq"];

  static bool? IImageFileFormat<WsqFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == 0xFF && header[1] == 0xA0
      ? true : null;

  static WsqFile IImageFileFormat<WsqFile>.FromFile(FileInfo file) => WsqReader.FromFile(file);
  static WsqFile IImageFileFormat<WsqFile>.FromBytes(byte[] data) => WsqReader.FromBytes(data);
  static WsqFile IImageFileFormat<WsqFile>.FromStream(Stream stream) => WsqReader.FromStream(stream);
  static byte[] IImageFileFormat<WsqFile>.ToBytes(WsqFile file) => WsqWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Always 8 for WSQ.</summary>
  public int BitDepth => 8;

  /// <summary>Pixels per inch (typically 500 for fingerprints).</summary>
  public int Ppi { get; init; } = 500;

  /// <summary>Compression ratio (0.0-1.0 quality, higher = better quality, lower compression).</summary>
  public double CompressionRatio { get; init; } = 0.75;

  /// <summary>8-bit grayscale pixel data in row-major order.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(WsqFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
