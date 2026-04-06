using System;
using FileFormat.Core;

namespace FileFormat.CiscoIp;

/// <summary>In-memory representation of a Cisco IP Phone image image.</summary>
public readonly record struct CiscoIpFile : IImageFormatReader<CiscoIpFile>, IImageToRawImage<CiscoIpFile>, IImageFromRawImage<CiscoIpFile>, IImageFormatWriter<CiscoIpFile> {

  internal const int HeaderSize = 80;

  static string IImageFormatMetadata<CiscoIpFile>.PrimaryExtension => ".cip";
  static string[] IImageFormatMetadata<CiscoIpFile>.FileExtensions => [".cip"];
  static CiscoIpFile IImageFormatReader<CiscoIpFile>.FromSpan(ReadOnlySpan<byte> data) => CiscoIpReader.FromSpan(data);
  static byte[] IImageFormatWriter<CiscoIpFile>.ToBytes(CiscoIpFile file) => CiscoIpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CiscoIpFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CiscoIpFile FromRawImage(RawImage image) {
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
