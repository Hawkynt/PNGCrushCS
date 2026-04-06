using System;
using FileFormat.Core;

namespace FileFormat.SunIcon;

/// <summary>In-memory representation of a Sun Icon (.icon) image.</summary>
[FormatMagicBytes([0x2F, 0x2A, 0x20])]
public readonly record struct SunIconFile : IImageFormatReader<SunIconFile>, IImageToRawImage<SunIconFile>, IImageFromRawImage<SunIconFile>, IImageFormatWriter<SunIconFile> {

  static string IImageFormatMetadata<SunIconFile>.PrimaryExtension => ".icon";
  static string[] IImageFormatMetadata<SunIconFile>.FileExtensions => [".icon"];
  static SunIconFile IImageFormatReader<SunIconFile>.FromSpan(ReadOnlySpan<byte> data) => SunIconReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<SunIconFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<SunIconFile>.ToBytes(SunIconFile file) => SunIconWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  // 1 = foreground (black), 0 = background (white)
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(SunIconFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
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
