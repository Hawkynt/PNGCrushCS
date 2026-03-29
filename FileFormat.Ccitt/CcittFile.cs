using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ccitt;

/// <summary>In-memory representation of a CCITT-compressed bi-level image.</summary>
public sealed class CcittFile : IImageFileFormat<CcittFile> {

  static string IImageFileFormat<CcittFile>.PrimaryExtension => ".g3";
  static string[] IImageFileFormat<CcittFile>.FileExtensions => [".g3", ".g4", ".ccitt"];
  static CcittFile IImageFileFormat<CcittFile>.FromFile(FileInfo file) => throw new NotSupportedException("CCITT files require external width, height, and format parameters. Use CcittReader.FromFile(FileInfo, int, int, CcittFormat) instead.");
  static RawImage IImageFileFormat<CcittFile>.ToRawImage(CcittFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<CcittFile>.ToBytes(CcittFile file) => CcittWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public CcittFormat Format { get; init; }

  /// <summary>1bpp packed pixel data (MSB first, ceil(width/8) bytes per row).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = Core.PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static CcittFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != Core.PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = CcittFormat.Group4,
      PixelData = image.PixelData[..],
    };
  }
}
