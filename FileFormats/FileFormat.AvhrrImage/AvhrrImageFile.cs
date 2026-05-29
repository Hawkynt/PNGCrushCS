using System;
using FileFormat.Core;

namespace FileFormat.AvhrrImage;

/// <summary>In-memory representation of an AVHRR satellite grayscale image.</summary>
public readonly record struct AvhrrImageFile : IImageFormatReader<AvhrrImageFile>, IImageToRawImage<AvhrrImageFile>, IImageFromRawImage<AvhrrImageFile>, IImageFormatWriter<AvhrrImageFile> {

  static string IImageFormatMetadata<AvhrrImageFile>.PrimaryExtension => ".sst";
  static string[] IImageFormatMetadata<AvhrrImageFile>.FileExtensions => [".sst"];
  static AvhrrImageFile IImageFormatReader<AvhrrImageFile>.FromSpan(ReadOnlySpan<byte> data) => AvhrrImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<AvhrrImageFile>.ToBytes(AvhrrImageFile file) => AvhrrImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "AVHR" (0x41 0x56 0x48 0x52).</summary>
  internal static readonly byte[] Magic = [0x41, 0x56, 0x48, 0x52];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bands(2) + dataType(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of spectral bands.</summary>
  public ushort Bands { get; init; }

  /// <summary>Data type identifier.</summary>
  public ushort DataType { get; init; }

  /// <summary>Raw grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AvhrrImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static AvhrrImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bands = 1,
      DataType = 1,
      PixelData = image.PixelData[..],
    };
  }
}
