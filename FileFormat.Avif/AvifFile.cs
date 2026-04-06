using System;
using FileFormat.Core;

namespace FileFormat.Avif;

/// <summary>In-memory representation of an AVIF (AV1 Image File Format) image.</summary>
public readonly record struct AvifFile : IImageFormatReader<AvifFile>, IImageToRawImage<AvifFile>, IImageFromRawImage<AvifFile>, IImageFormatWriter<AvifFile> {

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

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Major brand from the ftyp box (e.g. "avif" or "avis").</summary>
  public string Brand { get; init; }

  /// <summary>Raw image data bytes from the mdat box (AV1 OBUs when read from an external file, or raw pixels when created by us).</summary>
  public byte[] RawImageData { get; init; }

  public static RawImage ToRawImage(AvifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static AvifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var pixelData = image.PixelData[..];
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
      RawImageData = pixelData[..],
    };
  }
}
