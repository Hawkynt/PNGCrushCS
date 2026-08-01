using System;
using FileFormat.Core;

namespace FileFormat.Hrz;

/// <summary>In-memory representation of a HRZ (slow-scan television) image.</summary>
[FormatMimeType("image/x-hrz")]
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

  /// <summary>
  /// Expands the file's six-bit samples to the eight bits a <see cref="RawImage"/> carries.
  /// </summary>
  /// <remarks>
  /// HRZ stores each channel in six bits, 0..63, which is what a slow-scan television frame carried.
  /// The samples used to be handed over untouched, so a fully saturated red arrived as 63 out of 255
  /// and every HRZ image was four times too dark. The top bits are repeated into the bottom ones so
  /// that 63 reaches a true 255 rather than stopping at 252.
  /// </remarks>
  public static RawImage ToRawImage(HrzFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = _ToEightBit(file.PixelData),
  };

  /// <summary>Six bits to eight, repeating the high bits into the low ones.</summary>
  private static byte[] _ToEightBit(byte[] samples) {
    var result = new byte[samples.Length];
    for (var i = 0; i < samples.Length; ++i) {
      var six = samples[i] & 0x3F;
      result[i] = (byte)((six << 2) | (six >> 4));
    }

    return result;
  }

  public static HrzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width != 256 || image.Height != 240)
      throw new ArgumentException($"Expected 256x240 but got {image.Width}x{image.Height}.", nameof(image));

    // Back down to the six bits the format stores.
    var samples = new byte[image.PixelData.Length];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (byte)(image.PixelData[i] >> 2);

    return new() { PixelData = samples };
  }
}
