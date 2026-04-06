using System;
using FileFormat.Core;

namespace FileFormat.Pi;

/// <summary>In-memory representation of a Pi format (NEC PC-88/98) image.</summary>
public readonly record struct PiFile : IImageFormatReader<PiFile>, IImageToRawImage<PiFile>, IImageFromRawImage<PiFile>, IImageFormatWriter<PiFile> {

  internal const int HeaderSize = 18;

  static string IImageFormatMetadata<PiFile>.PrimaryExtension => ".pi";
  static string[] IImageFormatMetadata<PiFile>.FileExtensions => [".pi"];
  static PiFile IImageFormatReader<PiFile>.FromSpan(ReadOnlySpan<byte> data) => PiReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PiFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<PiFile>.ToBytes(PiFile file) => PiWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PiFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static PiFile FromRawImage(RawImage image) {
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
