using System;
using FileFormat.Core;

namespace FileFormat.AdexImage;

/// <summary>In-memory representation of an ADEX image.</summary>
public readonly record struct AdexImageFile : IImageFormatReader<AdexImageFile>, IImageToRawImage<AdexImageFile>, IImageFormatWriter<AdexImageFile> {

  static string IImageFormatMetadata<AdexImageFile>.PrimaryExtension => ".adx";
  static string[] IImageFormatMetadata<AdexImageFile>.FileExtensions => [".adx"];
  static AdexImageFile IImageFormatReader<AdexImageFile>.FromSpan(ReadOnlySpan<byte> data) => AdexImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<AdexImageFile>.ToBytes(AdexImageFile file) => AdexImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "ADEX" (0x41 0x44 0x45 0x58).</summary>
  internal static readonly byte[] Magic = [0x41, 0x44, 0x45, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + compression(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Compression type.</summary>
  public ushort Compression { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this ADEX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AdexImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

}
