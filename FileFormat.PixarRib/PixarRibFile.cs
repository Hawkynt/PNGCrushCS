using System;
using FileFormat.Core;

namespace FileFormat.PixarRib;

/// <summary>In-memory representation of a Pixar RIB texture image.</summary>
public readonly record struct PixarRibFile : IImageFormatReader<PixarRibFile>, IImageToRawImage<PixarRibFile>, IImageFromRawImage<PixarRibFile>, IImageFormatWriter<PixarRibFile> {

  internal const int HeaderSize = 512;

  static string IImageFormatMetadata<PixarRibFile>.PrimaryExtension => ".pxr";
  static string[] IImageFormatMetadata<PixarRibFile>.FileExtensions => [".pxr", ".pixar", ".picio"];
  static PixarRibFile IImageFormatReader<PixarRibFile>.FromSpan(ReadOnlySpan<byte> data) => PixarRibReader.FromSpan(data);
  static byte[] IImageFormatWriter<PixarRibFile>.ToBytes(PixarRibFile file) => PixarRibWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PixarRibFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PixarRibFile FromRawImage(RawImage image) {
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
