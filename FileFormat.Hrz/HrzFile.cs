using System;
using FileFormat.Core;

namespace FileFormat.Hrz;

/// <summary>In-memory representation of a HRZ (slow-scan television) image.</summary>
public readonly record struct HrzFile :
  IImageFormatReader<HrzFile>, IImageToRawImage<HrzFile>,
  IImageFromRawImage<HrzFile>, IImageFormatWriter<HrzFile>,
  IImageInfoReader<HrzFile> {

  static string IImageFormatMetadata<HrzFile>.PrimaryExtension => ".hrz";
  static string[] IImageFormatMetadata<HrzFile>.FileExtensions => [".hrz"];
  static HrzFile IImageFormatReader<HrzFile>.FromSpan(ReadOnlySpan<byte> data) => HrzReader.FromSpan(data);
  static byte[] IImageFormatWriter<HrzFile>.ToBytes(HrzFile file) => HrzWriter.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB pixel data (3 bytes per pixel, 184320 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header)
    => header.Length == 256 * 240 * 3 ? new(256, 240, 24, "Rgb24") : null;

  public static RawImage ToRawImage(HrzFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static HrzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));
    if (image.Width != 256 || image.Height != 240)
      throw new ArgumentException($"Expected 256x240 but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
