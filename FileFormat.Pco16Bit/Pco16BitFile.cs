using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pco16Bit;

/// <summary>In-memory representation of a PCO 16-bit grayscale image.</summary>
public sealed class Pco16BitFile : IImageFileFormat<Pco16BitFile> {

  static string IImageFileFormat<Pco16BitFile>.PrimaryExtension => ".b16";
  static string[] IImageFileFormat<Pco16BitFile>.FileExtensions => [".b16"];
  static Pco16BitFile IImageFileFormat<Pco16BitFile>.FromFile(FileInfo file) => Pco16BitReader.FromFile(file);
  static Pco16BitFile IImageFileFormat<Pco16BitFile>.FromBytes(byte[] data) => Pco16BitReader.FromBytes(data);
  static Pco16BitFile IImageFileFormat<Pco16BitFile>.FromStream(Stream stream) => Pco16BitReader.FromStream(stream);
  static byte[] IImageFileFormat<Pco16BitFile>.ToBytes(Pco16BitFile file) => Pco16BitWriter.ToBytes(file);

  /// <summary>Header size: width(4) + height(4) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>16-bit LE grayscale pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this PCO image to a platform-independent <see cref="RawImage"/> in Gray16 format.</summary>
  public static RawImage ToRawImage(Pco16BitFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Creates a PCO 16-bit grayscale file from a <see cref="RawImage"/>.</summary>
  public static Pco16BitFile FromRawImage(RawImage image) {
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
        // Expand 8-bit to 16-bit LE by shifting into high byte
        for (var i = 0; i < pixelCount; ++i) {
          pixelData[i * 2] = 0;
          pixelData[i * 2 + 1] = image.PixelData[i];
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
