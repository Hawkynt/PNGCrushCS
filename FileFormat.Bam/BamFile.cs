using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bam;

/// <summary>Basic Alpha Matte — a minimal RGBA image format used as a reference implementation for the split-interface vision.
/// Layout: 16-byte header (magic "BAMF" + u32 version + u32 width + u32 height) followed by Width*Height*4 bytes of RGBA pixel data (big-endian dimensions).</summary>
[FormatMagicBytes([0x42, 0x41, 0x4D, 0x46])] // "BAMF"
public readonly record struct BamFile :
  IImageFormatReader<BamFile>,
  IImageToRawImage<BamFile>,
  IImageFromRawImage<BamFile>,
  IImageFormatWriter<BamFile>,
  IImageInfoReader<BamFile> {

  internal const int HeaderSize = 16;
  internal const uint Version = 1;

  public static string PrimaryExtension => ".bam";
  public static string[] FileExtensions => [".bam"];

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>RGBA pixel data, Width * Height * 4 bytes.</summary>
  public byte[] PixelData { get; init; }

  public static BamFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HeaderSize)
      throw new InvalidDataException($"BAM file too small: expected at least {HeaderSize} bytes, got {data.Length}.");

    if (data[0] != 'B' || data[1] != 'A' || data[2] != 'M' || data[3] != 'F')
      throw new InvalidDataException("BAM magic bytes not found.");

    var version = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    if (version != Version)
      throw new InvalidDataException($"Unsupported BAM version: {version} (expected {Version}).");

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[12..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid BAM dimensions: {width}x{height}.");

    var expectedSize = width * height * 4;
    if (data.Length < HeaderSize + expectedSize)
      throw new InvalidDataException($"BAM pixel data truncated: expected {expectedSize} bytes, got {data.Length - HeaderSize}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data.Slice(HeaderSize, expectedSize).ToArray(),
    };
  }

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;
    if (header[0] != 'B' || header[1] != 'A' || header[2] != 'M' || header[3] != 'F')
      return null;

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
    return new(width, height, 32, "Rgba32");
  }

  public static RawImage ToRawImage(BamFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgba32,
    PixelData = file.PixelData[..],
  };

  public static BamFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }

  public static byte[] ToBytes(BamFile file) {
    var pixelBytes = file.Width * file.Height * 4;
    var result = new byte[HeaderSize + pixelBytes];
    var span = result.AsSpan();

    span[0] = (byte)'B'; span[1] = (byte)'A'; span[2] = (byte)'M'; span[3] = (byte)'F';
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], Version);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)file.Width);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)file.Height);
    file.PixelData.AsSpan(0, pixelBytes).CopyTo(span[HeaderSize..]);

    return result;
  }
}
