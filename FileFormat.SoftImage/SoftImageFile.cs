using System;
using FileFormat.Core;

namespace FileFormat.SoftImage;

/// <summary>In-memory representation of a Softimage PIC image.</summary>
[FormatMagicBytes([0x53, 0x80, 0xF6, 0x34])]
public readonly record struct SoftImageFile : IImageFormatReader<SoftImageFile>, IImageToRawImage<SoftImageFile>, IImageFromRawImage<SoftImageFile>, IImageFormatWriter<SoftImageFile> {

  /// <summary>Magic number identifying a Softimage PIC file (0x5380F634).</summary>
  internal const uint Magic = 0x5380F634;

  /// <summary>Size of the fixed header in bytes.</summary>
  internal const int HeaderSize = 100;

  /// <summary>Size of the comment field in bytes.</summary>
  internal const int CommentSize = 80;

  static string IImageFormatMetadata<SoftImageFile>.PrimaryExtension => ".pic";
  static string[] IImageFormatMetadata<SoftImageFile>.FileExtensions => [".pic", ".si"];
  static SoftImageFile IImageFormatReader<SoftImageFile>.FromSpan(ReadOnlySpan<byte> data) => SoftImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<SoftImageFile>.ToBytes(SoftImageFile file) => SoftImageWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data (RGB or RGBA, interleaved, 8 bits per component).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>80-byte ASCII comment from the file header.</summary>
  public string Comment { get; init; }

  /// <summary>Whether the image contains an alpha channel.</summary>
  public bool HasAlpha { get; init; }

  /// <summary>File format version (float32).</summary>
  public float Version { get; init; }

  /// <summary>Converts a Softimage PIC file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(SoftImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a Softimage PIC file from a <see cref="RawImage"/>. Accepts Rgb24 or Rgba32.</summary>
  public static SoftImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24 && image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} or {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      HasAlpha = image.Format == PixelFormat.Rgba32,
      PixelData = image.PixelData[..],
      Version = 3.71f,
    };
  }
}
