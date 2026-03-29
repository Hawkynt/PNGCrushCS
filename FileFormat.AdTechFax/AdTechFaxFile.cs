using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AdTechFax;

/// <summary>In-memory representation of an AdTech fax image.</summary>
public sealed class AdTechFaxFile : IImageFileFormat<AdTechFaxFile> {

  static string IImageFileFormat<AdTechFaxFile>.PrimaryExtension => ".adt";
  static string[] IImageFileFormat<AdTechFaxFile>.FileExtensions => [".adt"];
  static AdTechFaxFile IImageFileFormat<AdTechFaxFile>.FromFile(FileInfo file) => AdTechFaxReader.FromFile(file);
  static AdTechFaxFile IImageFileFormat<AdTechFaxFile>.FromBytes(byte[] data) => AdTechFaxReader.FromBytes(data);
  static AdTechFaxFile IImageFileFormat<AdTechFaxFile>.FromStream(Stream stream) => AdTechFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<AdTechFaxFile>.ToBytes(AdTechFaxFile file) => AdTechFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "ADTF" (0x41 0x44 0x54 0x46).</summary>
  internal static readonly byte[] Magic = [0x41, 0x44, 0x54, 0x46];

  /// <summary>Header size: magic(4) + width(2) + height(2) + resolution(2) + reserved(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Scan resolution in DPI.</summary>
  public ushort Resolution { get; init; }

  /// <summary>Reserved field.</summary>
  public ushort Reserved { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this ADT image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AdTechFaxFile file) {
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
  public static AdTechFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to AdTechFaxFile is not supported.");
  }
}
