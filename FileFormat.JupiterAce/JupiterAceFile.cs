using System;
using FileFormat.Core;

namespace FileFormat.JupiterAce;

/// <summary>In-memory representation of a Jupiter Ace character screen image.</summary>
public readonly record struct JupiterAceFile : IImageFormatReader<JupiterAceFile>, IImageToRawImage<JupiterAceFile>, IImageFromRawImage<JupiterAceFile>, IImageFormatWriter<JupiterAceFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 192;
  internal const int FileSize = 1536;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<JupiterAceFile>.PrimaryExtension => ".jac";
  static string[] IImageFormatMetadata<JupiterAceFile>.FileExtensions => [".jac", ".ace"];
  static JupiterAceFile IImageFormatReader<JupiterAceFile>.FromSpan(ReadOnlySpan<byte> data) => JupiterAceReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<JupiterAceFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<JupiterAceFile>.ToBytes(JupiterAceFile file) => JupiterAceWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(JupiterAceFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static JupiterAceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
