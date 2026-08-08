using System;
using FileFormat.Core;

namespace FileFormat.Avs;

/// <summary>In-memory representation of an AVS (Application Visualization System) image.</summary>
public readonly record struct AvsFile : IImageFormatReader<AvsFile>, IImageToRawImage<AvsFile>, IImageFromRawImage<AvsFile>, IImageFormatWriter<AvsFile> {

  static string IImageFormatMetadata<AvsFile>.PrimaryExtension => ".avs";
  /// <summary>
  /// Also <c>.x</c>, which is what the AVS distribution itself names its sample images. Nothing in
  /// the file says so — there is no signature, only two lengths — but the reader requires the
  /// header and the pixels to account for the file to the byte, so a <c>.x</c> belonging to one of
  /// the several other formats using that name is refused rather than drawn wrongly.
  /// </summary>
  static string[] IImageFormatMetadata<AvsFile>.FileExtensions => [".avs", ".x"];
  static AvsFile IImageFormatReader<AvsFile>.FromSpan(ReadOnlySpan<byte> data) => AvsReader.FromSpan(data);
  static byte[] IImageFormatWriter<AvsFile>.ToBytes(AvsFile file) => AvsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw ARGB pixel data (4 bytes per pixel, big-endian).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AvsFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Argb32,
      PixelData = file.PixelData[..],
    };
  }

  public static AvsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Argb32);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
