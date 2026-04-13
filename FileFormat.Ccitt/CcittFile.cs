using System;
using FileFormat.Core;

namespace FileFormat.Ccitt;

/// <summary>In-memory representation of a CCITT-compressed bi-level image.</summary>
public sealed class CcittFile :
  IImageFormatReader<CcittFile>, IImageToRawImage<CcittFile>,
  IImageFromRawImage<CcittFile>, IImageFormatWriter<CcittFile> {

  static string IImageFormatMetadata<CcittFile>.PrimaryExtension => ".g3";
  static string[] IImageFormatMetadata<CcittFile>.FileExtensions => [".g3", ".g4", ".ccitt"];
  static CcittFile IImageFormatReader<CcittFile>.FromSpan(ReadOnlySpan<byte> data) => throw new NotSupportedException("CCITT files require external width, height, and format parameters. Use CcittReader.FromBytes(byte[], int, int, CcittFormat) instead.");
  static byte[] IImageFormatWriter<CcittFile>.ToBytes(CcittFile file) => CcittWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public CcittFormat Format { get; init; }

  /// <summary>1bpp packed pixel data (MSB first, ceil(width/8) bytes per row).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(CcittFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = Core.PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
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
