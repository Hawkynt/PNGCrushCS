using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cmu;

/// <summary>In-memory representation of a CMU (CMU Window Manager Bitmap) image.</summary>
public sealed class CmuFile : IImageFileFormat<CmuFile> {

  static string IImageFileFormat<CmuFile>.PrimaryExtension => ".cmu";
  static string[] IImageFileFormat<CmuFile>.FileExtensions => [".cmu"];
  static FormatCapability IImageFileFormat<CmuFile>.Capabilities => FormatCapability.MonochromeOnly;
  static CmuFile IImageFileFormat<CmuFile>.FromFile(FileInfo file) => CmuReader.FromFile(file);
  static CmuFile IImageFileFormat<CmuFile>.FromBytes(byte[] data) => CmuReader.FromBytes(data);
  static CmuFile IImageFileFormat<CmuFile>.FromStream(Stream stream) => CmuReader.FromStream(stream);
  static RawImage IImageFileFormat<CmuFile>.ToRawImage(CmuFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<CmuFile>.ToBytes(CmuFile file) => CmuWriter.ToBytes(file);
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

  public static CmuFile FromRawImage(RawImage image) {
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
