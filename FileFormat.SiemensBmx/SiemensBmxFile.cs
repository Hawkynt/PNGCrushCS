using System;
using FileFormat.Core;

namespace FileFormat.SiemensBmx;

/// <summary>In-memory representation of a Siemens mobile bitmap image.</summary>
public readonly record struct SiemensBmxFile : IImageFormatReader<SiemensBmxFile>, IImageToRawImage<SiemensBmxFile>, IImageFromRawImage<SiemensBmxFile>, IImageFormatWriter<SiemensBmxFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<SiemensBmxFile>.PrimaryExtension => ".bmx";
  static string[] IImageFormatMetadata<SiemensBmxFile>.FileExtensions => [".bmx"];
  static SiemensBmxFile IImageFormatReader<SiemensBmxFile>.FromSpan(ReadOnlySpan<byte> data) => SiemensBmxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<SiemensBmxFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<SiemensBmxFile>.ToBytes(SiemensBmxFile file) => SiemensBmxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SiemensBmxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static SiemensBmxFile FromRawImage(RawImage image) {
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
