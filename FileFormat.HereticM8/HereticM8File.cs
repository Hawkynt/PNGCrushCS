using System;
using FileFormat.Core;

namespace FileFormat.HereticM8;

/// <summary>In-memory representation of a Heretic II MipMap texture image.</summary>
public readonly record struct HereticM8File : IImageFormatReader<HereticM8File>, IImageToRawImage<HereticM8File>, IImageFromRawImage<HereticM8File>, IImageFormatWriter<HereticM8File> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<HereticM8File>.PrimaryExtension => ".m8";
  static string[] IImageFormatMetadata<HereticM8File>.FileExtensions => [".m8"];
  static HereticM8File IImageFormatReader<HereticM8File>.FromSpan(ReadOnlySpan<byte> data) => HereticM8Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HereticM8File>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<HereticM8File>.ToBytes(HereticM8File file) => HereticM8Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(HereticM8File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static HereticM8File FromRawImage(RawImage image) {
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
