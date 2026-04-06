using System;
using FileFormat.Core;

namespace FileFormat.Calamus;

/// <summary>In-memory representation of a Calamus raster image.</summary>
public readonly record struct CalamusFile : IImageFormatReader<CalamusFile>, IImageToRawImage<CalamusFile>, IImageFormatWriter<CalamusFile> {

  static string IImageFormatMetadata<CalamusFile>.PrimaryExtension => ".cpi";
  static string[] IImageFormatMetadata<CalamusFile>.FileExtensions => [".cpi", ".crg"];
  static CalamusFile IImageFormatReader<CalamusFile>.FromSpan(ReadOnlySpan<byte> data) => CalamusReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CalamusFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<CalamusFile>.ToBytes(CalamusFile file) => CalamusWriter.ToBytes(file);

  /// <summary>Magic bytes: "CALM" (0x43 0x41 0x4C 0x4D).</summary>
  internal static readonly byte[] Magic = [0x43, 0x41, 0x4C, 0x4D];

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

  /// <summary>Bits per pixel (always 1 for monochrome).</summary>
  public ushort Bpp { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Calamus image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CalamusFile file) {

    var bytesPerRow = (file.Width + 7) / 8;
    var rgb = new byte[file.Width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (file.PixelData[byteIndex] >> bitIndex) & 1;
        var offset = (y * file.Width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
