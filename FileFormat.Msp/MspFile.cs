using System;
using FileFormat.Core;

namespace FileFormat.Msp;

/// <summary>In-memory representation of a Microsoft Paint (MSP) image.</summary>
[FormatMagicBytes([0x44, 0x61, 0x68, 0x6E])]
[FormatMagicBytes([0x4C, 0x69, 0x6E, 0x53])]
public readonly record struct MspFile : IImageFormatReader<MspFile>, IImageToRawImage<MspFile>, IImageFromRawImage<MspFile>, IImageFormatWriter<MspFile> {

  static string IImageFormatMetadata<MspFile>.PrimaryExtension => ".msp";
  static string[] IImageFormatMetadata<MspFile>.FileExtensions => [".msp"];
  static MspFile IImageFormatReader<MspFile>.FromSpan(ReadOnlySpan<byte> data) => MspReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MspFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<MspFile>.ToBytes(MspFile file) => MspWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public MspVersion Version { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(MspFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static MspFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Version = MspVersion.V2,
      PixelData = image.PixelData[..],
    };
  }
}
