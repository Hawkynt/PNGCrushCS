using System;
using FileFormat.Core;

namespace FileFormat.AccessFax;

/// <summary>In-memory representation of an AccessFax G4 image.</summary>
public readonly record struct AccessFaxFile : IImageFormatReader<AccessFaxFile>, IImageToRawImage<AccessFaxFile>, IImageFormatWriter<AccessFaxFile> {

  static string IImageFormatMetadata<AccessFaxFile>.PrimaryExtension => ".g4";
  static string[] IImageFormatMetadata<AccessFaxFile>.FileExtensions => [".g4", ".acc"];
  static AccessFaxFile IImageFormatReader<AccessFaxFile>.FromSpan(ReadOnlySpan<byte> data) => AccessFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<AccessFaxFile>.ToBytes(AccessFaxFile file) => AccessFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: 0x00 0x00.</summary>
  internal static readonly byte[] Magic = [0x00, 0x00];

  /// <summary>Header size: magic(2) + width(2) + height(2) + flags(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this AccessFax image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AccessFaxFile file) {

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
