using System;
using FileFormat.Core;

namespace FileFormat.Fpx;

/// <summary>In-memory representation of an FPX (FlashPix) image.</summary>
[FormatMagicBytes([0x46, 0x50, 0x58, 0x00])]
public readonly record struct FpxFile : IImageFormatReader<FpxFile>, IImageToRawImage<FpxFile>, IImageFromRawImage<FpxFile>, IImageFormatWriter<FpxFile> {

  static string IImageFormatMetadata<FpxFile>.PrimaryExtension => ".fpx";
  static string[] IImageFormatMetadata<FpxFile>.FileExtensions => [".fpx"];
  static FpxFile IImageFormatReader<FpxFile>.FromSpan(ReadOnlySpan<byte> data) => FpxReader.FromSpan(data);
  static byte[] IImageFormatWriter<FpxFile>.ToBytes(FpxFile file) => FpxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FpxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static FpxFile FromRawImage(RawImage image) {
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
