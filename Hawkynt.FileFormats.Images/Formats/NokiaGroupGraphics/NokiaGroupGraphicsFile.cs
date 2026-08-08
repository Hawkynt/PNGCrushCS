using System;
using FileFormat.Core;

namespace FileFormat.NokiaGroupGraphics;

/// <summary>In-memory representation of a Nokia Group Graphics (NGG) image.</summary>
public readonly record struct NokiaGroupGraphicsFile : IImageFormatReader<NokiaGroupGraphicsFile>, IImageToRawImage<NokiaGroupGraphicsFile>, IImageFromRawImage<NokiaGroupGraphicsFile>, IImageFormatWriter<NokiaGroupGraphicsFile> {

  static string IImageFormatMetadata<NokiaGroupGraphicsFile>.PrimaryExtension => ".ngg";
  static string[] IImageFormatMetadata<NokiaGroupGraphicsFile>.FileExtensions => [".ngg"];
  static NokiaGroupGraphicsFile IImageFormatReader<NokiaGroupGraphicsFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaGroupGraphicsReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<NokiaGroupGraphicsFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<NokiaGroupGraphicsFile>.ToBytes(NokiaGroupGraphicsFile file) => NokiaGroupGraphicsWriter.ToBytes(file);

  /// <summary>Magic bytes: "NGG" (0x4E 0x47 0x47).</summary>
  internal static readonly byte[] Magic = [0x4E, 0x47, 0x47];

  /// <summary>Header size: magic(3) + version(1) + width(1) + height(1) = 6 bytes.</summary>
  internal const int HeaderSize = 6;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version.</summary>
  public byte Version { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this NGG image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(NokiaGroupGraphicsFile file) {

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

  /// <summary>Builds a group graphic from any picture, sampling it down when the header cannot name its size.</summary>
  /// <remarks>
  /// Width and height are single bytes, so 255 is the ceiling — a phone screen of the day was far
  /// smaller anyway. A set bit is a lit pixel on the monochrome display, which the decoder paints
  /// black.
  /// </remarks>
  public static NokiaGroupGraphicsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min(image.Width, byte.MaxValue);
    var height = Math.Min(image.Height, byte.MaxValue);
    var pixels = BilevelRows.Threshold(image.SampleTo(width, height), setWhenDark: true);

    return new() {
      Width = width,
      Height = height,
      Version = 1,
      PixelData = BilevelRows.Pack(pixels, width, height),
    };
  }

}
