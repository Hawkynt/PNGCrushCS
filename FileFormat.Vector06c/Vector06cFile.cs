using System;
using FileFormat.Core;

namespace FileFormat.Vector06c;

/// <summary>In-memory representation of a Vector-06C screen image.</summary>
public readonly record struct Vector06cFile : IImageFormatReader<Vector06cFile>, IImageToRawImage<Vector06cFile>, IImageFromRawImage<Vector06cFile>, IImageFormatWriter<Vector06cFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 256;
  internal const int FileSize = 16384;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 255, 0, 255, 0, 255, 0, 0];

  static string IImageFormatMetadata<Vector06cFile>.PrimaryExtension => ".v06";
  static string[] IImageFormatMetadata<Vector06cFile>.FileExtensions => [".v06", ".scr"];
  static Vector06cFile IImageFormatReader<Vector06cFile>.FromSpan(ReadOnlySpan<byte> data) => Vector06cReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<Vector06cFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<Vector06cFile>.ToBytes(Vector06cFile file) => Vector06cWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Vector06cFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _DefaultPalette[..],
      PaletteCount = 4,
    };
  }

  public static Vector06cFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
