using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CanonNavFax;

/// <summary>In-memory representation of a Canon Navigator Fax CAN image.</summary>
public sealed class CanonNavFaxFile : IImageFileFormat<CanonNavFaxFile> {

  static string IImageFileFormat<CanonNavFaxFile>.PrimaryExtension => ".can";
  static string[] IImageFileFormat<CanonNavFaxFile>.FileExtensions => [".can"];
  static CanonNavFaxFile IImageFileFormat<CanonNavFaxFile>.FromFile(FileInfo file) => CanonNavFaxReader.FromFile(file);
  static CanonNavFaxFile IImageFileFormat<CanonNavFaxFile>.FromBytes(byte[] data) => CanonNavFaxReader.FromBytes(data);
  static CanonNavFaxFile IImageFileFormat<CanonNavFaxFile>.FromStream(Stream stream) => CanonNavFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<CanonNavFaxFile>.ToBytes(CanonNavFaxFile file) => CanonNavFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "CNFX" (0x43 0x4E 0x46 0x58).</summary>
  internal static readonly byte[] Magic = [0x43, 0x4E, 0x46, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + resolution(2) + encoding(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Scan resolution in DPI.</summary>
  public ushort Resolution { get; init; }

  /// <summary>Encoding type (0 = uncompressed).</summary>
  public ushort Encoding { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this CAN image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CanonNavFaxFile file) {
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
  public static CanonNavFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CanonNavFaxFile is not supported.");
  }
}
