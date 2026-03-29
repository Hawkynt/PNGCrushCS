using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HayesJtfax;

/// <summary>In-memory representation of a Hayes JT Fax image.</summary>
public sealed class HayesJtfaxFile : IImageFileFormat<HayesJtfaxFile> {

  static string IImageFileFormat<HayesJtfaxFile>.PrimaryExtension => ".jtf";
  static string[] IImageFileFormat<HayesJtfaxFile>.FileExtensions => [".jtf"];
  static FormatCapability IImageFileFormat<HayesJtfaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static HayesJtfaxFile IImageFileFormat<HayesJtfaxFile>.FromFile(FileInfo file) => HayesJtfaxReader.FromFile(file);
  static HayesJtfaxFile IImageFileFormat<HayesJtfaxFile>.FromBytes(byte[] data) => HayesJtfaxReader.FromBytes(data);
  static HayesJtfaxFile IImageFileFormat<HayesJtfaxFile>.FromStream(Stream stream) => HayesJtfaxReader.FromStream(stream);
  static byte[] IImageFileFormat<HayesJtfaxFile>.ToBytes(HayesJtfaxFile file) => HayesJtfaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "JT" (0x4A 0x54).</summary>
  internal static readonly byte[] Magic = [0x4A, 0x54];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) + reserved(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this JTF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HayesJtfaxFile file) {
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
  public static HayesJtfaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to HayesJtfaxFile is not supported.");
  }
}
