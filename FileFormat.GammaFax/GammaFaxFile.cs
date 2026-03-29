using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GammaFax;

/// <summary>In-memory representation of a GammaFax GMF image.</summary>
public sealed class GammaFaxFile : IImageFileFormat<GammaFaxFile> {

  static string IImageFileFormat<GammaFaxFile>.PrimaryExtension => ".gmf";
  static string[] IImageFileFormat<GammaFaxFile>.FileExtensions => [".gmf"];
  static FormatCapability IImageFileFormat<GammaFaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static GammaFaxFile IImageFileFormat<GammaFaxFile>.FromFile(FileInfo file) => GammaFaxReader.FromFile(file);
  static GammaFaxFile IImageFileFormat<GammaFaxFile>.FromBytes(byte[] data) => GammaFaxReader.FromBytes(data);
  static GammaFaxFile IImageFileFormat<GammaFaxFile>.FromStream(Stream stream) => GammaFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<GammaFaxFile>.ToBytes(GammaFaxFile file) => GammaFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "GF" (0x47 0x46).</summary>
  internal static readonly byte[] Magic = [0x47, 0x46];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) + compression(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this GMF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(GammaFaxFile file) {
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
  public static GammaFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to GammaFaxFile is not supported.");
  }
}
