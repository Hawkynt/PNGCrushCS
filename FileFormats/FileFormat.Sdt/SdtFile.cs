using System;
using FileFormat.Core;

namespace FileFormat.Sdt;

/// <summary>In-memory representation of a SmartDraw thumbnail image.</summary>
public readonly record struct SdtFile : IImageFormatReader<SdtFile>, IImageToRawImage<SdtFile>, IImageFromRawImage<SdtFile>, IImageFormatWriter<SdtFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<SdtFile>.PrimaryExtension => ".sdt";
  static string[] IImageFormatMetadata<SdtFile>.FileExtensions => [".sdt"];
  static SdtFile IImageFormatReader<SdtFile>.FromSpan(ReadOnlySpan<byte> data) => SdtReader.FromSpan(data);
  static byte[] IImageFormatWriter<SdtFile>.ToBytes(SdtFile file) => SdtWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SdtFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static SdtFile FromRawImage(RawImage image) {
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
