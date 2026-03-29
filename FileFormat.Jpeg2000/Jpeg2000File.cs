using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jpeg2000;

/// <summary>In-memory representation of a JPEG 2000 image.</summary>
[FormatMagicBytes([0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50])]
public sealed class Jpeg2000File : IImageFileFormat<Jpeg2000File> {

  static string IImageFileFormat<Jpeg2000File>.PrimaryExtension => ".jp2";
  static string[] IImageFileFormat<Jpeg2000File>.FileExtensions => [".jp2", ".j2k", ".j2c", ".jpx", ".jpc", ".jpf", ".jpt", ".jpm"];
  static Jpeg2000File IImageFileFormat<Jpeg2000File>.FromFile(FileInfo file) => Jpeg2000Reader.FromFile(file);
  static Jpeg2000File IImageFileFormat<Jpeg2000File>.FromBytes(byte[] data) => Jpeg2000Reader.FromBytes(data);
  static Jpeg2000File IImageFileFormat<Jpeg2000File>.FromStream(Stream stream) => Jpeg2000Reader.FromStream(stream);
  static byte[] IImageFileFormat<Jpeg2000File>.ToBytes(Jpeg2000File file) => Jpeg2000Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of image components (1 for grayscale, 3 for RGB).</summary>
  public int ComponentCount { get; init; } = 3;

  /// <summary>Bits per component (always 8 in this implementation).</summary>
  public int BitsPerComponent { get; init; } = 8;

  /// <summary>Number of DWT decomposition levels used.</summary>
  public int DecompositionLevels { get; init; } = 3;

  /// <summary>Raw pixel data in Gray8 (1 component) or Rgb24 (3 components) layout.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Jpeg2000File file) {
    ArgumentNullException.ThrowIfNull(file);
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
