using System;
using FileFormat.Core;

namespace FileFormat.RedStormRsb;

/// <summary>In-memory representation of a Red Storm RSB image.</summary>
public readonly record struct RedStormRsbFile : IImageFormatReader<RedStormRsbFile>, IImageToRawImage<RedStormRsbFile>, IImageFromRawImage<RedStormRsbFile>, IImageFormatWriter<RedStormRsbFile> {

  static string IImageFormatMetadata<RedStormRsbFile>.PrimaryExtension => ".rsb";
  static string[] IImageFormatMetadata<RedStormRsbFile>.FileExtensions => [".rsb"];
  static RedStormRsbFile IImageFormatReader<RedStormRsbFile>.FromSpan(ReadOnlySpan<byte> data) => RedStormRsbReader.FromSpan(data);
  static byte[] IImageFormatWriter<RedStormRsbFile>.ToBytes(RedStormRsbFile file) => RedStormRsbWriter.ToBytes(file);

  /// <summary>Magic bytes: "RSB\0" (0x52 0x53 0x42 0x00).</summary>
  internal static readonly byte[] Magic = [0x52, 0x53, 0x42, 0x00];

  /// <summary>Header size in bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this RSB image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(RedStormRsbFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a Red Storm RSB file from a <see cref="RawImage"/>. Accepts Rgb24.</summary>
  public static RedStormRsbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bpp = 24,
      PixelData = image.PixelData[..],
    };
  }
}
