using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>In-memory representation of a HEIF/HEIC (ISO/IEC 23008-12) image at the container level.</summary>
public sealed class HeifFile : IImageFileFormat<HeifFile> {

  static string IImageFileFormat<HeifFile>.PrimaryExtension => ".heic";
  static string[] IImageFileFormat<HeifFile>.FileExtensions => [".heic", ".heif"];
  static HeifFile IImageFileFormat<HeifFile>.FromFile(FileInfo file) => HeifReader.FromFile(file);
  static HeifFile IImageFileFormat<HeifFile>.FromBytes(byte[] data) => HeifReader.FromBytes(data);
  static HeifFile IImageFileFormat<HeifFile>.FromStream(Stream stream) => HeifReader.FromStream(stream);
  static byte[] IImageFileFormat<HeifFile>.ToBytes(HeifFile file) => HeifWriter.ToBytes(file);

  static bool? IImageFileFormat<HeifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
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
  public byte[] PixelData { get; init; } = [];

  /// <summary>The major brand from the ftyp box (e.g. "heic", "heix", "hevc", "mif1").</summary>
  public string Brand { get; init; } = "heic";

  /// <summary>Raw image payload data stored in the mdat box (HEVC NAL units or uncompressed).</summary>
  public byte[] RawImageData { get; init; } = [];

  public static RawImage ToRawImage(HeifFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
