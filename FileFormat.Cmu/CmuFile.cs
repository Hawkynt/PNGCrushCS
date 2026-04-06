using System;
using FileFormat.Core;

namespace FileFormat.Cmu;

/// <summary>In-memory representation of a CMU (CMU Window Manager Bitmap) image.</summary>
public readonly record struct CmuFile : IImageFormatReader<CmuFile>, IImageToRawImage<CmuFile>, IImageFromRawImage<CmuFile>, IImageFormatWriter<CmuFile> {

  static string IImageFormatMetadata<CmuFile>.PrimaryExtension => ".cmu";
  static string[] IImageFormatMetadata<CmuFile>.FileExtensions => [".cmu"];
  static CmuFile IImageFormatReader<CmuFile>.FromSpan(ReadOnlySpan<byte> data) => CmuReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CmuFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<CmuFile>.ToBytes(CmuFile file) => CmuWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(CmuFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
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
