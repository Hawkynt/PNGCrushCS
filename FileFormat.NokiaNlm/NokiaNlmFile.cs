using System;
using FileFormat.Core;

namespace FileFormat.NokiaNlm;

/// <summary>In-memory representation of a Nokia Logo Manager image image.</summary>
public readonly record struct NokiaNlmFile : IImageFormatReader<NokiaNlmFile>, IImageToRawImage<NokiaNlmFile>, IImageFromRawImage<NokiaNlmFile>, IImageFormatWriter<NokiaNlmFile> {

  internal const int FixedWidth = 84;
  internal const int FixedHeight = 48;
  internal const int FileSize = 508;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<NokiaNlmFile>.PrimaryExtension => ".nlm";
  static string[] IImageFormatMetadata<NokiaNlmFile>.FileExtensions => [".nlm"];
  static NokiaNlmFile IImageFormatReader<NokiaNlmFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaNlmReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NokiaNlmFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<NokiaNlmFile>.ToBytes(NokiaNlmFile file) => NokiaNlmWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(NokiaNlmFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static NokiaNlmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
