using System;
using FileFormat.Core;

namespace FileFormat.HfImage;

/// <summary>In-memory representation of an HF height field image.</summary>
public readonly record struct HfImageFile : IImageFormatReader<HfImageFile>, IImageToRawImage<HfImageFile>, IImageFromRawImage<HfImageFile>, IImageFormatWriter<HfImageFile> {

  static string IImageFormatMetadata<HfImageFile>.PrimaryExtension => ".hf";
  static string[] IImageFormatMetadata<HfImageFile>.FileExtensions => [".hf"];
  static HfImageFile IImageFormatReader<HfImageFile>.FromSpan(ReadOnlySpan<byte> data) => HfImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<HfImageFile>.ToBytes(HfImageFile file) => HfImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "HF" (0x48 0x46).</summary>
  internal static readonly byte[] Magic = [0x48, 0x46];

  /// <summary>Header size: magic(2) + width(2) + height(2) + dataType(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Data type identifier.</summary>
  public ushort DataType { get; init; }

  /// <summary>Raw grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(HfImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static HfImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      DataType = 1,
      PixelData = image.PixelData[..],
    };
  }
}
