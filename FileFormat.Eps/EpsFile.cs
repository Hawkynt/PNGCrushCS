using System;
using FileFormat.Core;

namespace FileFormat.Eps;

/// <summary>In-memory representation of an EPS (Encapsulated PostScript) image with embedded TIFF preview.</summary>
[FormatMagicBytes([0xC5, 0xD0, 0xD3, 0xC6])]
public readonly record struct EpsFile : IImageFormatReader<EpsFile>, IImageToRawImage<EpsFile>, IImageFromRawImage<EpsFile>, IImageFormatWriter<EpsFile> {

  static string IImageFormatMetadata<EpsFile>.PrimaryExtension => ".eps";
  static string[] IImageFormatMetadata<EpsFile>.FileExtensions => [".eps", ".epsf", ".epsi", ".epi", ".ept"];
  static EpsFile IImageFormatReader<EpsFile>.FromSpan(ReadOnlySpan<byte> data) => EpsReader.FromSpan(data);
  static byte[] IImageFormatWriter<EpsFile>.ToBytes(EpsFile file) => EpsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel) from the embedded TIFF preview.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(EpsFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static EpsFile FromRawImage(RawImage image) {
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
