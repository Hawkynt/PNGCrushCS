using System;
using FileFormat.Core;

namespace FileFormat.Grs16;

/// <summary>In-memory representation of a headerless raw 16-bit grayscale image.</summary>
public readonly record struct Grs16File : IImageFormatReader<Grs16File>, IImageToRawImage<Grs16File>, IImageFromRawImage<Grs16File>, IImageFormatWriter<Grs16File> {

  static string IImageFormatMetadata<Grs16File>.PrimaryExtension => ".g16";
  static string[] IImageFormatMetadata<Grs16File>.FileExtensions => [".g16"];
  static Grs16File IImageFormatReader<Grs16File>.FromSpan(ReadOnlySpan<byte> data) => Grs16Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Grs16File>.ToBytes(Grs16File file) => Grs16Writer.ToBytes(file);

  /// <summary>Minimum valid file size (at least one 16-bit pixel).</summary>
  public const int MinFileSize = 2;

  /// <summary>Default assumed width when dimensions cannot be inferred.</summary>
  internal const int DefaultWidth = 256;

  /// <summary>Default assumed height when dimensions cannot be inferred.</summary>
  internal const int DefaultHeight = 256;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw 16-bit little-endian grayscale pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Grs16File file) {

    var pixelCount = file.Width * file.Height;
    var gray16 = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var dstOffset = i * 2;
      if (srcOffset + 1 < file.PixelData.Length) {
        // Source is LE (lo, hi); Gray16 is BE (hi, lo)
        gray16[dstOffset] = file.PixelData[srcOffset + 1];
        gray16[dstOffset + 1] = file.PixelData[srcOffset];
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray16,
      PixelData = gray16,
    };
  }

  public static Grs16File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var pixelCount = image.Width * image.Height;
    var pixelData = new byte[pixelCount * 2];

    switch (image.Format) {
      case PixelFormat.Gray16:
        // Gray16 is BE (hi, lo); stored format is LE (lo, hi)
        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          pixelData[i * 2] = image.PixelData[srcOffset + 1];
          pixelData[i * 2 + 1] = image.PixelData[srcOffset];
        }
        break;
      case PixelFormat.Gray8:
        // Expand 8-bit to 16-bit LE by duplicating into both bytes
        for (var i = 0; i < pixelCount; ++i) {
          var value = image.PixelData[i];
          pixelData[i * 2] = value;
          pixelData[i * 2 + 1] = value;
        }
        break;
      default:
        throw new ArgumentException($"Expected {PixelFormat.Gray16} or {PixelFormat.Gray8} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
    };
  }
}
