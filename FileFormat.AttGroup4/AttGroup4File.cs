using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AttGroup4;

/// <summary>In-memory representation of an AT&amp;T Group 4 fax image.</summary>
public sealed class AttGroup4File : IImageFileFormat<AttGroup4File> {

  static string IImageFileFormat<AttGroup4File>.PrimaryExtension => ".att";
  static string[] IImageFileFormat<AttGroup4File>.FileExtensions => [".att"];
  static AttGroup4File IImageFileFormat<AttGroup4File>.FromFile(FileInfo file) => AttGroup4Reader.FromFile(file);
  static AttGroup4File IImageFileFormat<AttGroup4File>.FromBytes(byte[] data) => AttGroup4Reader.FromBytes(data);
  static AttGroup4File IImageFileFormat<AttGroup4File>.FromStream(Stream stream) => AttGroup4Reader.FromStream(stream);
  static byte[] IImageFileFormat<AttGroup4File>.ToBytes(AttGroup4File file) => AttGroup4Writer.ToBytes(file);

  /// <summary>Magic bytes: "ATT\0" (0x41 0x54 0x54 0x00).</summary>
  internal static readonly byte[] Magic = [0x41, 0x54, 0x54, 0x00];

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

  /// <summary>Converts this ATT image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AttGroup4File file) {
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
  public static AttGroup4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to AttGroup4File is not supported.");
  }
}
