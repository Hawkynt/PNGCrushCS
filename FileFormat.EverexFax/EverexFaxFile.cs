using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EverexFax;

/// <summary>In-memory representation of an Everex Fax EFX image.</summary>
public sealed class EverexFaxFile : IImageFileFormat<EverexFaxFile> {

  static string IImageFileFormat<EverexFaxFile>.PrimaryExtension => ".efx";
  static string[] IImageFileFormat<EverexFaxFile>.FileExtensions => [".efx", ".ef3"];
  static EverexFaxFile IImageFileFormat<EverexFaxFile>.FromFile(FileInfo file) => EverexFaxReader.FromFile(file);
  static EverexFaxFile IImageFileFormat<EverexFaxFile>.FromBytes(byte[] data) => EverexFaxReader.FromBytes(data);
  static EverexFaxFile IImageFileFormat<EverexFaxFile>.FromStream(Stream stream) => EverexFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<EverexFaxFile>.ToBytes(EverexFaxFile file) => EverexFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "EFAX" (0x45 0x46 0x41 0x58).</summary>
  internal static readonly byte[] Magic = [0x45, 0x46, 0x41, 0x58];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + pages(2) + compression(2) + reserved(2) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Number of pages.</summary>
  public ushort Pages { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>Reserved field.</summary>
  public ushort Reserved { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this EFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(EverexFaxFile file) {
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
  public static EverexFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to EverexFaxFile is not supported.");
  }
}
