using System;
using FileFormat.Core;

namespace FileFormat.Rlc2;

/// <summary>In-memory representation of an RLC2 image.</summary>
public readonly record struct Rlc2File : IImageFormatReader<Rlc2File>, IImageToRawImage<Rlc2File>, IImageFormatWriter<Rlc2File> {

  static string IImageFormatMetadata<Rlc2File>.PrimaryExtension => ".rlc";
  static string[] IImageFormatMetadata<Rlc2File>.FileExtensions => [".rlc"];
  static Rlc2File IImageFormatReader<Rlc2File>.FromSpan(ReadOnlySpan<byte> data) => Rlc2Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Rlc2File>.ToBytes(Rlc2File file) => Rlc2Writer.ToBytes(file);

  /// <summary>Magic bytes: "RLC2" (0x52 0x4C 0x43 0x32).</summary>
  internal static readonly byte[] Magic = [0x52, 0x4C, 0x43, 0x32];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this RLC2 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Rlc2File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

}
