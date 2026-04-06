using System;
using FileFormat.Core;

namespace FileFormat.Q0;

/// <summary>In-memory representation of a Q0 raw RGB format image.</summary>
public readonly record struct Q0File : IImageFormatReader<Q0File>, IImageToRawImage<Q0File>, IImageFromRawImage<Q0File>, IImageFormatWriter<Q0File> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<Q0File>.PrimaryExtension => ".q0";
  static string[] IImageFormatMetadata<Q0File>.FileExtensions => [".q0"];
  static Q0File IImageFormatReader<Q0File>.FromSpan(ReadOnlySpan<byte> data) => Q0Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Q0File>.ToBytes(Q0File file) => Q0Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Q0File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static Q0File FromRawImage(RawImage image) {
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
