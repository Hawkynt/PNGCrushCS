using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FremontFax;

/// <summary>In-memory representation of a Fremont Fax F96 image.</summary>
public sealed class FremontFaxFile : IImageFileFormat<FremontFaxFile> {

  static string IImageFileFormat<FremontFaxFile>.PrimaryExtension => ".f96";
  static string[] IImageFileFormat<FremontFaxFile>.FileExtensions => [".f96"];
  static FremontFaxFile IImageFileFormat<FremontFaxFile>.FromFile(FileInfo file) => FremontFaxReader.FromFile(file);
  static FremontFaxFile IImageFileFormat<FremontFaxFile>.FromBytes(byte[] data) => FremontFaxReader.FromBytes(data);
  static FremontFaxFile IImageFileFormat<FremontFaxFile>.FromStream(Stream stream) => FremontFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<FremontFaxFile>.ToBytes(FremontFaxFile file) => FremontFaxWriter.ToBytes(file);

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
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this F96 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FremontFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Not supported.</summary>
  public static FremontFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FremontFaxFile is not supported.");
  }
}
