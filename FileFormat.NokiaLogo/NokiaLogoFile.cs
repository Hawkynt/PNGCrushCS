using System;
using FileFormat.Core;

namespace FileFormat.NokiaLogo;

/// <summary>In-memory representation of a Nokia Operator Logo image.</summary>
public readonly record struct NokiaLogoFile : IImageFormatReader<NokiaLogoFile>, IImageToRawImage<NokiaLogoFile>, IImageFromRawImage<NokiaLogoFile>, IImageFormatWriter<NokiaLogoFile> {

  internal const int FixedWidth = 72;
  internal const int FixedHeight = 14;
  internal const int FileSize = 131;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<NokiaLogoFile>.PrimaryExtension => ".nol";
  static string[] IImageFormatMetadata<NokiaLogoFile>.FileExtensions => [".nol", ".ngg"];
  static NokiaLogoFile IImageFormatReader<NokiaLogoFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaLogoReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NokiaLogoFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<NokiaLogoFile>.ToBytes(NokiaLogoFile file) => NokiaLogoWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(NokiaLogoFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static NokiaLogoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
