using System;
using FileFormat.Core;

namespace FileFormat.Awd;

/// <summary>In-memory representation of an AWD (Microsoft Fax) image.</summary>
[FormatMagicBytes([0x41, 0x57, 0x44, 0x00])]
public readonly record struct AwdFile : IImageFormatReader<AwdFile>, IImageToRawImage<AwdFile>, IImageFromRawImage<AwdFile>, IImageFormatWriter<AwdFile> {

  static string IImageFormatMetadata<AwdFile>.PrimaryExtension => ".awd";
  static string[] IImageFormatMetadata<AwdFile>.FileExtensions => [".awd"];
  static AwdFile IImageFormatReader<AwdFile>.FromSpan(ReadOnlySpan<byte> data) => AwdReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AwdFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<AwdFile>.ToBytes(AwdFile file) => AwdWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp pixel data, MSB-first, rows padded to byte boundaries.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(AwdFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static AwdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
