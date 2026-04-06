using System;
using FileFormat.Core;

namespace FileFormat.BfxBitware;

/// <summary>In-memory representation of a Bitware BFX fax image.</summary>
public readonly record struct BfxBitwareFile : IImageFormatReader<BfxBitwareFile>, IImageToRawImage<BfxBitwareFile>, IImageFormatWriter<BfxBitwareFile> {

  static string IImageFormatMetadata<BfxBitwareFile>.PrimaryExtension => ".bfx";
  static string[] IImageFormatMetadata<BfxBitwareFile>.FileExtensions => [".bfx"];
  static BfxBitwareFile IImageFormatReader<BfxBitwareFile>.FromSpan(ReadOnlySpan<byte> data) => BfxBitwareReader.FromSpan(data);
  static byte[] IImageFormatWriter<BfxBitwareFile>.ToBytes(BfxBitwareFile file) => BfxBitwareWriter.ToBytes(file);

  /// <summary>Magic bytes: "BFX\0" (0x42 0x46 0x58 0x00).</summary>
  internal static readonly byte[] Magic = [0x42, 0x46, 0x58, 0x00];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + compression(2) + reserved(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

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
  public byte[] PixelData { get; init; }

  /// <summary>Converts this BFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(BfxBitwareFile file) {

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
