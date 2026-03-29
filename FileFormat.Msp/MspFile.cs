using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Msp;

/// <summary>In-memory representation of a Microsoft Paint (MSP) image.</summary>
[FormatMagicBytes([0x44, 0x61, 0x68, 0x6E])]
[FormatMagicBytes([0x4C, 0x69, 0x6E, 0x53])]
public sealed class MspFile : IImageFileFormat<MspFile> {

  static string IImageFileFormat<MspFile>.PrimaryExtension => ".msp";
  static string[] IImageFileFormat<MspFile>.FileExtensions => [".msp"];
  static FormatCapability IImageFileFormat<MspFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MspFile IImageFileFormat<MspFile>.FromFile(FileInfo file) => MspReader.FromFile(file);
  static MspFile IImageFileFormat<MspFile>.FromBytes(byte[] data) => MspReader.FromBytes(data);
  static MspFile IImageFileFormat<MspFile>.FromStream(Stream stream) => MspReader.FromStream(stream);
  static RawImage IImageFileFormat<MspFile>.ToRawImage(MspFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<MspFile>.ToBytes(MspFile file) => MspWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public MspVersion Version { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
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
