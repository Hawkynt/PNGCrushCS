using System;
using FileFormat.Core;

namespace FileFormat.Dragon;

/// <summary>In-memory representation of a Dragon 32/64 PMODE 4 screen image.</summary>
public readonly record struct DragonFile : IImageFormatReader<DragonFile>, IImageToRawImage<DragonFile>, IImageFromRawImage<DragonFile>, IImageFormatWriter<DragonFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 192;
  internal const int FileSize = 6144;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<DragonFile>.PrimaryExtension => ".dgn";
  static string[] IImageFormatMetadata<DragonFile>.FileExtensions => [".dgn"];
  static DragonFile IImageFormatReader<DragonFile>.FromSpan(ReadOnlySpan<byte> data) => DragonReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<DragonFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<DragonFile>.ToBytes(DragonFile file) => DragonWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DragonFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static DragonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
