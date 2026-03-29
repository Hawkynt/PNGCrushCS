using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OazFax;

/// <summary>In-memory representation of an OazFax OAZ/XFX image.</summary>
public sealed class OazFaxFile : IImageFileFormat<OazFaxFile> {

  static string IImageFileFormat<OazFaxFile>.PrimaryExtension => ".oaz";
  static string[] IImageFileFormat<OazFaxFile>.FileExtensions => [".oaz", ".xfx"];
  static OazFaxFile IImageFileFormat<OazFaxFile>.FromFile(FileInfo file) => OazFaxReader.FromFile(file);
  static OazFaxFile IImageFileFormat<OazFaxFile>.FromBytes(byte[] data) => OazFaxReader.FromBytes(data);
  static OazFaxFile IImageFileFormat<OazFaxFile>.FromStream(Stream stream) => OazFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<OazFaxFile>.ToBytes(OazFaxFile file) => OazFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "OAZF" (0x4F 0x41 0x5A 0x46).</summary>
  internal static readonly byte[] Magic = [0x4F, 0x41, 0x5A, 0x46];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + encoding(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Encoding type (0 = uncompressed).</summary>
  public ushort Encoding { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this OAZ image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(OazFaxFile file) {
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
  public static OazFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to OazFaxFile is not supported.");
  }
}
