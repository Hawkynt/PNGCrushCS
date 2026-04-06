using System;
using FileFormat.Core;

namespace FileFormat.Jpeg2000;

/// <summary>In-memory representation of a JPEG 2000 image.</summary>
[FormatMagicBytes([0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50])]
public readonly record struct Jpeg2000File : IImageFormatReader<Jpeg2000File>, IImageToRawImage<Jpeg2000File>, IImageFromRawImage<Jpeg2000File>, IImageFormatWriter<Jpeg2000File> {

  static string IImageFormatMetadata<Jpeg2000File>.PrimaryExtension => ".jp2";
  static string[] IImageFormatMetadata<Jpeg2000File>.FileExtensions => [".jp2", ".j2k", ".j2c", ".jpx", ".jpc", ".jpf", ".jpt", ".jpm"];
  static Jpeg2000File IImageFormatReader<Jpeg2000File>.FromSpan(ReadOnlySpan<byte> data) => Jpeg2000Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Jpeg2000File>.ToBytes(Jpeg2000File file) => Jpeg2000Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of image components (1 for grayscale, 3 for RGB).</summary>
  public int ComponentCount { get; init; }

  /// <summary>Bits per component (always 8 in this implementation).</summary>
  public int BitsPerComponent { get; init; }

  /// <summary>Number of DWT decomposition levels used.</summary>
  public int DecompositionLevels { get; init; }

  /// <summary>Raw pixel data in Gray8 (1 component) or Rgb24 (3 components) layout.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Jpeg2000File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.ComponentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static Jpeg2000File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    int componentCount;
    byte[] pixelData;
    if (image.Format == PixelFormat.Gray8) {
      componentCount = 1;
      pixelData = image.PixelData[..];
    } else if (image.Format == PixelFormat.Rgb24) {
      componentCount = 3;
      pixelData = image.PixelData[..];
    } else
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };
  }
}
