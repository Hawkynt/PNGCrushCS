using System;
using FileFormat.Core;

namespace FileFormat.Avif;

/// <summary>In-memory representation of an AVIF (AV1 Image File Format) image.</summary>
/// <remarks>
/// Reading covers what AVIF still pictures actually contain: an AV1 key frame, 8 to 12 bits,
/// monochrome or 4:2:0, 4:2:2 or 4:4:4, with an alpha auxiliary item when the file carries one.
/// Writing produces a lossless 4:4:4 key frame with the identity colour matrix, so the file
/// round-trips exactly and other AV1 decoders read it back sample for sample. Anything outside that
/// — inter prediction, film grain, scalability, super-resolution, palette blocks, intra block copy —
/// is refused by name rather than approximated.
/// </remarks>
[FormatMimeType("image/avif", "image/avif-sequence")]
[VerifiedBy(ConformanceOracle.ImageMagick, ConformanceOracle.AvifDec, ConformanceOracle.FFmpeg, ConformanceOracle.IrfanView)]
public readonly record struct AvifFile
  : IImageFormatReader<AvifFile>, IImageToRawImage<AvifFile>, IImageFromRawImage<AvifFile>, IImageFormatWriter<AvifFile> {

  static string IImageFormatMetadata<AvifFile>.PrimaryExtension => ".avif";
  static string[] IImageFormatMetadata<AvifFile>.FileExtensions => [".avif"];
  static AvifFile IImageFormatReader<AvifFile>.FromSpan(ReadOnlySpan<byte> data) => AvifReader.FromSpan(data);
  static byte[] IImageFormatWriter<AvifFile>.ToBytes(AvifFile file) => AvifWriter.ToBytes(file);

  static bool? IImageFormatMetadata<AvifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || header[4] != 0x66 || header[5] != 0x74 || header[6] != 0x79 || header[7] != 0x70)
      return null;
    if (header[8] == (byte)'a' && header[9] == (byte)'v' && header[10] == (byte)'i' && header[11] == (byte)'f')
      return true;
    if (header[8] == (byte)'a' && header[9] == (byte)'v' && header[10] == (byte)'i' && header[11] == (byte)'s')
      return true;
    return null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Pixel data, RGB24 or RGBA32 depending on <see cref="HasAlpha"/>.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Whether the file carried an alpha auxiliary item, making <see cref="PixelData"/> RGBA32.</summary>
  public bool HasAlpha { get; init; }

  /// <summary>Major brand from the ftyp box, "avif" or "avis".</summary>
  public string Brand { get; init; }

  /// <summary>The primary item's coded AV1 bytes, as read from or written to the file.</summary>
  public byte[] RawImageData { get; init; }

  public static RawImage ToRawImage(AvifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static AvifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The writer codes three planes. An image with alpha loses it here rather than in the encoder,
    // where the loss would be silent.
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Brand = "avif",
      RawImageData = [],
    };
  }
}
