using System;
using FileFormat.Core;

namespace FileFormat.MonoMagic;

/// <summary>In-memory representation of a Mono Magic C64 image image.</summary>
public readonly record struct MonoMagicFile : IImageFormatReader<MonoMagicFile>, IImageToRawImage<MonoMagicFile>, IImageFromRawImage<MonoMagicFile>, IImageFormatWriter<MonoMagicFile> {

  internal const int FixedWidth = 320;
  internal const int FixedHeight = 200;
  internal const int FileSize = 9009;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<MonoMagicFile>.PrimaryExtension => ".mon";
  static string[] IImageFormatMetadata<MonoMagicFile>.FileExtensions => [".mon"];
  static MonoMagicFile IImageFormatReader<MonoMagicFile>.FromSpan(ReadOnlySpan<byte> data) => MonoMagicReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MonoMagicFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<MonoMagicFile>.ToBytes(MonoMagicFile file) => MonoMagicWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MonoMagicFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static MonoMagicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
