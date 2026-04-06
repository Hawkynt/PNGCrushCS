using System;
using FileFormat.Core;

namespace FileFormat.Ipl;

/// <summary>In-memory representation of a IPL Image Sequence frame image.</summary>
public readonly record struct IplFile : IImageFormatReader<IplFile>, IImageToRawImage<IplFile>, IImageFromRawImage<IplFile>, IImageFormatWriter<IplFile> {

  internal const int HeaderSize = 16;

  static string IImageFormatMetadata<IplFile>.PrimaryExtension => ".ipl";
  static string[] IImageFormatMetadata<IplFile>.FileExtensions => [".ipl"];
  static IplFile IImageFormatReader<IplFile>.FromSpan(ReadOnlySpan<byte> data) => IplReader.FromSpan(data);
  static byte[] IImageFormatWriter<IplFile>.ToBytes(IplFile file) => IplWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IplFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static IplFile FromRawImage(RawImage image) {
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
