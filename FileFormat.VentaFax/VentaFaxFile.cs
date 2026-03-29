using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.VentaFax;

/// <summary>In-memory representation of a VentaFax VFX image.</summary>
public sealed class VentaFaxFile : IImageFileFormat<VentaFaxFile> {

  static string IImageFileFormat<VentaFaxFile>.PrimaryExtension => ".vfx";
  static string[] IImageFileFormat<VentaFaxFile>.FileExtensions => [".vfx"];
  static VentaFaxFile IImageFileFormat<VentaFaxFile>.FromFile(FileInfo file) => VentaFaxReader.FromFile(file);
  static VentaFaxFile IImageFileFormat<VentaFaxFile>.FromBytes(byte[] data) => VentaFaxReader.FromBytes(data);
  static VentaFaxFile IImageFileFormat<VentaFaxFile>.FromStream(Stream stream) => VentaFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<VentaFaxFile>.ToBytes(VentaFaxFile file) => VentaFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "VFAX" (0x56 0x46 0x41 0x58).</summary>
  internal static readonly byte[] Magic = [0x56, 0x46, 0x41, 0x58];

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

  /// <summary>Converts this VFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(VentaFaxFile file) {
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
  public static VentaFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to VentaFaxFile is not supported.");
  }
}
