using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SunIcon;

/// <summary>In-memory representation of a Sun Icon (.icon) image.</summary>
[FormatMagicBytes([0x2F, 0x2A, 0x20])]
public sealed class SunIconFile : IImageFileFormat<SunIconFile> {

  static string IImageFileFormat<SunIconFile>.PrimaryExtension => ".icon";
  static string[] IImageFileFormat<SunIconFile>.FileExtensions => [".icon"];
  static FormatCapability IImageFileFormat<SunIconFile>.Capabilities => FormatCapability.MonochromeOnly;
  static SunIconFile IImageFileFormat<SunIconFile>.FromFile(FileInfo file) => SunIconReader.FromFile(file);
  static SunIconFile IImageFileFormat<SunIconFile>.FromBytes(byte[] data) => SunIconReader.FromBytes(data);
  static SunIconFile IImageFileFormat<SunIconFile>.FromStream(Stream stream) => SunIconReader.FromStream(stream);
  static RawImage IImageFileFormat<SunIconFile>.ToRawImage(SunIconFile file) => file.ToRawImage();
  static SunIconFile IImageFileFormat<SunIconFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<SunIconFile>.ToBytes(SunIconFile file) => SunIconWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  // 1 = foreground (black), 0 = background (white)
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static SunIconFile FromRawImage(RawImage image) {
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
