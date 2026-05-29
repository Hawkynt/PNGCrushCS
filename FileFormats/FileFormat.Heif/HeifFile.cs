using System;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>In-memory representation of a HEIF/HEIC (ISO/IEC 23008-12) image at the container level.</summary>
public readonly record struct HeifFile : IImageFormatReader<HeifFile>, IImageToRawImage<HeifFile>, IImageFromRawImage<HeifFile>, IImageFormatWriter<HeifFile> {

  static string IImageFormatMetadata<HeifFile>.PrimaryExtension => ".heic";
  static string[] IImageFormatMetadata<HeifFile>.FileExtensions => [".heic", ".heif"];
  static HeifFile IImageFormatReader<HeifFile>.FromSpan(ReadOnlySpan<byte> data) => HeifReader.FromSpan(data);
  static byte[] IImageFormatWriter<HeifFile>.ToBytes(HeifFile file) => HeifWriter.ToBytes(file);

  static bool? IImageFormatMetadata<HeifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || header[4] != 0x66 || header[5] != 0x74 || header[6] != 0x79 || header[7] != 0x70)
      return null;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'x')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'v' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'m' && header[9] == (byte)'i' && header[10] == (byte)'f' && header[11] == (byte)'1')
      return true;
    return null;
  }

  /// <summary>Image width in pixels (from ispe box).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (from ispe box).</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data (Rgb24 format, 3 bytes per pixel) for container-level round-trip.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The major brand from the ftyp box (e.g. "heic", "heix", "hevc", "mif1").</summary>
  public string Brand { get; init; }

  /// <summary>Raw image payload data stored in the mdat box (HEVC NAL units or uncompressed).</summary>
  public byte[] RawImageData { get; init; }

  public static RawImage ToRawImage(HeifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static HeifFile FromRawImage(RawImage image) {
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
