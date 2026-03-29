using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MobileFax;

/// <summary>In-memory representation of a MobileFax RFA image.</summary>
public sealed class MobileFaxFile : IImageFileFormat<MobileFaxFile> {

  static string IImageFileFormat<MobileFaxFile>.PrimaryExtension => ".rfa";
  static string[] IImageFileFormat<MobileFaxFile>.FileExtensions => [".rfa"];
  static MobileFaxFile IImageFileFormat<MobileFaxFile>.FromFile(FileInfo file) => MobileFaxReader.FromFile(file);
  static MobileFaxFile IImageFileFormat<MobileFaxFile>.FromBytes(byte[] data) => MobileFaxReader.FromBytes(data);
  static MobileFaxFile IImageFileFormat<MobileFaxFile>.FromStream(Stream stream) => MobileFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<MobileFaxFile>.ToBytes(MobileFaxFile file) => MobileFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "MF" (0x4D 0x46).</summary>
  internal static readonly byte[] Magic = [0x4D, 0x46];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

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

  /// <summary>Converts this RFA image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MobileFaxFile file) {
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
  public static MobileFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MobileFaxFile is not supported.");
  }
}
