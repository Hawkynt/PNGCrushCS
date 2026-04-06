using System;
using FileFormat.Core;

namespace FileFormat.Otb;

/// <summary>In-memory representation of an OTB (Nokia Over-The-Air Bitmap) image.</summary>
public readonly record struct OtbFile : IImageFormatReader<OtbFile>, IImageToRawImage<OtbFile>, IImageFromRawImage<OtbFile>, IImageFormatWriter<OtbFile> {

  static string IImageFormatMetadata<OtbFile>.PrimaryExtension => ".otb";
  static string[] IImageFormatMetadata<OtbFile>.FileExtensions => [".otb"];
  static OtbFile IImageFormatReader<OtbFile>.FromSpan(ReadOnlySpan<byte> data) => OtbReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<OtbFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<OtbFile>.ToBytes(OtbFile file) => OtbWriter.ToBytes(file);
  /// <summary>Image width in pixels (1..255).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1..255).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(OtbFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static OtbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Width is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "OTB width must be in the range 1..255.");
    if (image.Height is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "OTB height must be in the range 1..255.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
