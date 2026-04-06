using System;
using FileFormat.Core;

namespace FileFormat.OazFax;

/// <summary>In-memory representation of an OazFax OAZ/XFX image.</summary>
public readonly record struct OazFaxFile : IImageFormatReader<OazFaxFile>, IImageToRawImage<OazFaxFile>, IImageFormatWriter<OazFaxFile> {

  static string IImageFormatMetadata<OazFaxFile>.PrimaryExtension => ".oaz";
  static string[] IImageFormatMetadata<OazFaxFile>.FileExtensions => [".oaz", ".xfx"];
  static OazFaxFile IImageFormatReader<OazFaxFile>.FromSpan(ReadOnlySpan<byte> data) => OazFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<OazFaxFile>.ToBytes(OazFaxFile file) => OazFaxWriter.ToBytes(file);

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
  public byte[] PixelData { get; init; }

  /// <summary>Converts this OAZ image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(OazFaxFile file) {

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

}
