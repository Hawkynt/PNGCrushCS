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

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<HrzFile>.VideoModes => [
    new("Default", [(256, 240)]),
  ];
  static byte[] IImageFormatWriter<HrzFile>.ToBytes(HrzFile file) => HrzWriter.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB pixel data (3 bytes per pixel, 184320 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header)
    => header.Length == 256 * 240 * 3 ? new(256, 240, 24, "Rgb24") : null;

  /// <summary>Widens the file's six-bit samples to the full byte each.</summary>
  /// <remarks>
  /// The format spends six bits a channel and stores each in its own byte, so the samples arrive
  /// somewhere between 0 and 63. Handing them over as they are makes the whole picture a quarter of
  /// its brightness — dark, but with every relationship between the colours intact, which is why it
  /// reads as a moody picture rather than a broken one.
  /// <para/>
  /// The widening repeats the sample's bits rather than shifting them up, so full scale comes out as
  /// 255 rather than 252.
  /// </remarks>
  public static RawImage ToRawImage(HrzFile file) {
    var pixels = new byte[file.PixelData.Length];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = ChannelScaling.Expand6(file.PixelData[i] & 0x3F);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  public static HrzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width != 256 || image.Height != 240)
      throw new ArgumentException($"Expected 256x240 but got {image.Width}x{image.Height}.", nameof(image));

    // Narrowing rounds rather than truncates, so a sample does not come back a step low.
    var pixels = new byte[image.PixelData.Length];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((image.PixelData[i] * 63 + 127) / 255);

    return new() { PixelData = pixels };
  }
}
