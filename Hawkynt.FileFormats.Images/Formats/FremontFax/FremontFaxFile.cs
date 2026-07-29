using System;
using FileFormat.Core;

namespace FileFormat.FremontFax;

/// <summary>In-memory representation of a Fremont Fax F96 image.</summary>
public readonly record struct FremontFaxFile : IImageFormatReader<FremontFaxFile>, IImageToRawImage<FremontFaxFile>, IImageFormatWriter<FremontFaxFile> {

  static string IImageFormatMetadata<FremontFaxFile>.PrimaryExtension => ".f96";
  static string[] IImageFormatMetadata<FremontFaxFile>.FileExtensions => [".f96"];
  static FremontFaxFile IImageFormatReader<FremontFaxFile>.FromSpan(ReadOnlySpan<byte> data) => FremontFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<FremontFaxFile>.ToBytes(FremontFaxFile file) => FremontFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "F96\0" (0x46 0x39 0x36 0x00).</summary>
  internal static readonly byte[] Magic = [0x46, 0x39, 0x36, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this F96 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FremontFaxFile file) {

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
