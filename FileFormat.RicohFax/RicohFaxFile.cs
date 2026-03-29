using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RicohFax;

/// <summary>In-memory representation of a RicohFax RIC image.</summary>
public sealed class RicohFaxFile : IImageFileFormat<RicohFaxFile> {

  static string IImageFileFormat<RicohFaxFile>.PrimaryExtension => ".ric";
  static string[] IImageFileFormat<RicohFaxFile>.FileExtensions => [".ric"];
  static RicohFaxFile IImageFileFormat<RicohFaxFile>.FromFile(FileInfo file) => RicohFaxReader.FromFile(file);
  static RicohFaxFile IImageFileFormat<RicohFaxFile>.FromBytes(byte[] data) => RicohFaxReader.FromBytes(data);
  static RicohFaxFile IImageFileFormat<RicohFaxFile>.FromStream(Stream stream) => RicohFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<RicohFaxFile>.ToBytes(RicohFaxFile file) => RicohFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "RICF" (0x52 0x49 0x43 0x46).</summary>
  internal static readonly byte[] Magic = [0x52, 0x49, 0x43, 0x46];

  /// <summary>Header size: magic(4) + width(2) + height(2) + resolution(2) + compression(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution value.</summary>
  public ushort Resolution { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this RIC image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(RicohFaxFile file) {
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
  public static RicohFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to RicohFaxFile is not supported.");
  }
}
