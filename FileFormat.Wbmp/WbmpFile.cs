using System;
using FileFormat.Core;

namespace FileFormat.Wbmp;

/// <summary>In-memory representation of a WBMP (Wireless Bitmap) image.</summary>
public readonly record struct WbmpFile : IImageFormatReader<WbmpFile>, IImageToRawImage<WbmpFile>, IImageFromRawImage<WbmpFile>, IImageFormatWriter<WbmpFile> {

  static string IImageFormatMetadata<WbmpFile>.PrimaryExtension => ".wbmp";
  static string[] IImageFormatMetadata<WbmpFile>.FileExtensions => [".wbmp"];
  static WbmpFile IImageFormatReader<WbmpFile>.FromSpan(ReadOnlySpan<byte> data) => WbmpReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<WbmpFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<WbmpFile>.ToBytes(WbmpFile file) => WbmpWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(WbmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
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
