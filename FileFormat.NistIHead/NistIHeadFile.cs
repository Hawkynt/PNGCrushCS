using System;
using FileFormat.Core;

namespace FileFormat.NistIHead;

/// <summary>In-memory representation of a NIST IHead grayscale image.</summary>
public readonly record struct NistIHeadFile : IImageFormatReader<NistIHeadFile>, IImageToRawImage<NistIHeadFile>, IImageFromRawImage<NistIHeadFile>, IImageFormatWriter<NistIHeadFile> {

  static string IImageFormatMetadata<NistIHeadFile>.PrimaryExtension => ".nst";
  static string[] IImageFormatMetadata<NistIHeadFile>.FileExtensions => [".nst"];
  static NistIHeadFile IImageFormatReader<NistIHeadFile>.FromSpan(ReadOnlySpan<byte> data) => NistIHeadReader.FromSpan(data);
  static byte[] IImageFormatWriter<NistIHeadFile>.ToBytes(NistIHeadFile file) => NistIHeadWriter.ToBytes(file);

  /// <summary>Magic bytes: "NIST" (0x4E 0x49 0x53 0x54).</summary>
  internal static readonly byte[] Magic = [0x4E, 0x49, 0x53, 0x54];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + compression(2) + reserved(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>Reserved bytes.</summary>
  public uint Reserved { get; init; }

  /// <summary>Raw grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(NistIHeadFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static NistIHeadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bpp = 8,
      PixelData = image.PixelData[..],
    };
  }
}
