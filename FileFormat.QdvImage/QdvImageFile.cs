using System;
using FileFormat.Core;

namespace FileFormat.QdvImage;

/// <summary>In-memory representation of a QDV image.</summary>
public readonly record struct QdvImageFile : IImageFormatReader<QdvImageFile>, IImageToRawImage<QdvImageFile>, IImageFormatWriter<QdvImageFile> {

  static string IImageFormatMetadata<QdvImageFile>.PrimaryExtension => ".qdv";
  static string[] IImageFormatMetadata<QdvImageFile>.FileExtensions => [".qdv"];
  static QdvImageFile IImageFormatReader<QdvImageFile>.FromSpan(ReadOnlySpan<byte> data) => QdvImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<QdvImageFile>.ToBytes(QdvImageFile file) => QdvImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "QDV\0" (0x51 0x44 0x56 0x00).</summary>
  internal static readonly byte[] Magic = [0x51, 0x44, 0x56, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + flags(2) = 12 bytes.</summary>
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

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this QDV image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(QdvImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

}
