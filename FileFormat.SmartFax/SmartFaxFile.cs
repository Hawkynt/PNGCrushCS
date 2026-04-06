using System;
using FileFormat.Core;

namespace FileFormat.SmartFax;

/// <summary>In-memory representation of a SmartFax SMF image.</summary>
public readonly record struct SmartFaxFile : IImageFormatReader<SmartFaxFile>, IImageToRawImage<SmartFaxFile>, IImageFormatWriter<SmartFaxFile> {

  static string IImageFormatMetadata<SmartFaxFile>.PrimaryExtension => ".smf";
  static string[] IImageFormatMetadata<SmartFaxFile>.FileExtensions => [".smf"];
  static SmartFaxFile IImageFormatReader<SmartFaxFile>.FromSpan(ReadOnlySpan<byte> data) => SmartFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<SmartFaxFile>.ToBytes(SmartFaxFile file) => SmartFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "SMFX" (0x53 0x4D 0x46 0x58).</summary>
  internal static readonly byte[] Magic = [0x53, 0x4D, 0x46, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + flags(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Flags field.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this SMF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SmartFaxFile file) {

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
