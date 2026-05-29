using System;
using FileFormat.Core;

namespace FileFormat.SegaSj1;

/// <summary>In-memory representation of a Sega SJ1 image.</summary>
public readonly record struct SegaSj1File : IImageFormatReader<SegaSj1File>, IImageToRawImage<SegaSj1File>, IImageFromRawImage<SegaSj1File>, IImageFormatWriter<SegaSj1File> {

  static string IImageFormatMetadata<SegaSj1File>.PrimaryExtension => ".sj1";
  static string[] IImageFormatMetadata<SegaSj1File>.FileExtensions => [".sj1"];
  static SegaSj1File IImageFormatReader<SegaSj1File>.FromSpan(ReadOnlySpan<byte> data) => SegaSj1Reader.FromSpan(data);
  static byte[] IImageFormatWriter<SegaSj1File>.ToBytes(SegaSj1File file) => SegaSj1Writer.ToBytes(file);

  /// <summary>Magic bytes: "SJ1\0" (0x53 0x4A 0x31 0x00).</summary>
  internal static readonly byte[] Magic = [0x53, 0x4A, 0x31, 0x00];

  /// <summary>Header size in bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this SJ1 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SegaSj1File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a Sega SJ1 file from a <see cref="RawImage"/>. Accepts Rgb24.</summary>
  public static SegaSj1File FromRawImage(RawImage image) {
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
