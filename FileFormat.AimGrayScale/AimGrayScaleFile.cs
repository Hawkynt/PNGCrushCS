using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AimGrayScale;

/// <summary>In-memory representation of an AIM grayscale image.</summary>
public sealed class AimGrayScaleFile : IImageFileFormat<AimGrayScaleFile> {

  static string IImageFileFormat<AimGrayScaleFile>.PrimaryExtension => ".aim";
  static string[] IImageFileFormat<AimGrayScaleFile>.FileExtensions => [".aim"];
  static AimGrayScaleFile IImageFileFormat<AimGrayScaleFile>.FromFile(FileInfo file) => AimGrayScaleReader.FromFile(file);
  static AimGrayScaleFile IImageFileFormat<AimGrayScaleFile>.FromBytes(byte[] data) => AimGrayScaleReader.FromBytes(data);
  static AimGrayScaleFile IImageFileFormat<AimGrayScaleFile>.FromStream(Stream stream) => AimGrayScaleReader.FromStream(stream);
  static byte[] IImageFileFormat<AimGrayScaleFile>.ToBytes(AimGrayScaleFile file) => AimGrayScaleWriter.ToBytes(file);

  /// <summary>Magic bytes: "AIM\0" (0x41 0x49 0x4D 0x00).</summary>
  internal static readonly byte[] Magic = [0x41, 0x49, 0x4D, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this AIM image to a platform-independent <see cref="RawImage"/> in Gray8 format.</summary>
  public static RawImage ToRawImage(AimGrayScaleFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an AimGrayScaleFile from a RawImage in Gray8 format.</summary>
  public static AimGrayScaleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
