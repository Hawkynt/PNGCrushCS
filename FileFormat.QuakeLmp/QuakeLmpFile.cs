using System;
using FileFormat.Core;

namespace FileFormat.QuakeLmp;

/// <summary>In-memory representation of a Quake LMP picture lump image.</summary>
public readonly record struct QuakeLmpFile : IImageFormatReader<QuakeLmpFile>, IImageToRawImage<QuakeLmpFile>, IImageFromRawImage<QuakeLmpFile>, IImageFormatWriter<QuakeLmpFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<QuakeLmpFile>.PrimaryExtension => ".lmp";
  static string[] IImageFormatMetadata<QuakeLmpFile>.FileExtensions => [".lmp"];
  static QuakeLmpFile IImageFormatReader<QuakeLmpFile>.FromSpan(ReadOnlySpan<byte> data) => QuakeLmpReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<QuakeLmpFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<QuakeLmpFile>.ToBytes(QuakeLmpFile file) => QuakeLmpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QuakeLmpFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static QuakeLmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
