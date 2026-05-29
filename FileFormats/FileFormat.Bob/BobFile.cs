using System;
using FileFormat.Core;

namespace FileFormat.Bob;

/// <summary>In-memory representation of a Bob Raytracer image image.</summary>
public readonly record struct BobFile : IImageFormatReader<BobFile>, IImageToRawImage<BobFile>, IImageFromRawImage<BobFile>, IImageFormatWriter<BobFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<BobFile>.PrimaryExtension => ".bob";
  static string[] IImageFormatMetadata<BobFile>.FileExtensions => [".bob"];
  static BobFile IImageFormatReader<BobFile>.FromSpan(ReadOnlySpan<byte> data) => BobReader.FromSpan(data);
  static byte[] IImageFormatWriter<BobFile>.ToBytes(BobFile file) => BobWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(BobFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static BobFile FromRawImage(RawImage image) {
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
