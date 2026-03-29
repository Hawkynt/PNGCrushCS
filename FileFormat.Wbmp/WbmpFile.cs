using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Wbmp;

/// <summary>In-memory representation of a WBMP (Wireless Bitmap) image.</summary>
public sealed class WbmpFile : IImageFileFormat<WbmpFile> {

  static string IImageFileFormat<WbmpFile>.PrimaryExtension => ".wbmp";
  static string[] IImageFileFormat<WbmpFile>.FileExtensions => [".wbmp"];
  static FormatCapability IImageFileFormat<WbmpFile>.Capabilities => FormatCapability.MonochromeOnly;
  static WbmpFile IImageFileFormat<WbmpFile>.FromFile(FileInfo file) => WbmpReader.FromFile(file);
  static WbmpFile IImageFileFormat<WbmpFile>.FromBytes(byte[] data) => WbmpReader.FromBytes(data);
  static WbmpFile IImageFileFormat<WbmpFile>.FromStream(Stream stream) => WbmpReader.FromStream(stream);
  static RawImage IImageFileFormat<WbmpFile>.ToRawImage(WbmpFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<WbmpFile>.ToBytes(WbmpFile file) => WbmpWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

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

  public static WbmpFile FromRawImage(RawImage image) {
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
