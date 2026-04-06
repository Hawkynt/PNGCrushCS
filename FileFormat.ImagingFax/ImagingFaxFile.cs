using System;
using FileFormat.Core;

namespace FileFormat.ImagingFax;

/// <summary>In-memory representation of an ImagingFax G3N image.</summary>
public readonly record struct ImagingFaxFile : IImageFormatReader<ImagingFaxFile>, IImageToRawImage<ImagingFaxFile>, IImageFormatWriter<ImagingFaxFile> {

  static string IImageFormatMetadata<ImagingFaxFile>.PrimaryExtension => ".g3n";
  static string[] IImageFormatMetadata<ImagingFaxFile>.FileExtensions => [".g3n"];
  static ImagingFaxFile IImageFormatReader<ImagingFaxFile>.FromSpan(ReadOnlySpan<byte> data) => ImagingFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<ImagingFaxFile>.ToBytes(ImagingFaxFile file) => ImagingFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "IMFX" (0x49 0x4D 0x46 0x58).</summary>
  internal static readonly byte[] Magic = [0x49, 0x4D, 0x46, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + encoding(2) + flags(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Encoding type (0 = uncompressed).</summary>
  public ushort Encoding { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this G3N image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ImagingFaxFile file) {

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
