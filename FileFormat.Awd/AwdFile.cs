using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Awd;

/// <summary>In-memory representation of an AWD (Microsoft Fax) image.</summary>
[FormatMagicBytes([0x41, 0x57, 0x44, 0x00])]
public sealed class AwdFile : IImageFileFormat<AwdFile> {

  static string IImageFileFormat<AwdFile>.PrimaryExtension => ".awd";
  static string[] IImageFileFormat<AwdFile>.FileExtensions => [".awd"];
  static FormatCapability IImageFileFormat<AwdFile>.Capabilities => FormatCapability.MonochromeOnly;
  static AwdFile IImageFileFormat<AwdFile>.FromFile(FileInfo file) => AwdReader.FromFile(file);
  static AwdFile IImageFileFormat<AwdFile>.FromBytes(byte[] data) => AwdReader.FromBytes(data);
  static AwdFile IImageFileFormat<AwdFile>.FromStream(Stream stream) => AwdReader.FromStream(stream);
  static RawImage IImageFileFormat<AwdFile>.ToRawImage(AwdFile file) => file.ToRawImage();
  static AwdFile IImageFileFormat<AwdFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AwdFile>.ToBytes(AwdFile file) => AwdWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp pixel data, MSB-first, rows padded to byte boundaries.</summary>
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
