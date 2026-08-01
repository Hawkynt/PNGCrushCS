using System;
using FileFormat.Core;

namespace FileFormat.Pcd;

/// <summary>In-memory representation of a PCD (Kodak Photo CD) image.</summary>
public readonly record struct PcdFile : IImageFormatReader<PcdFile>, IImageToRawImage<PcdFile>, IImageFromRawImage<PcdFile>, IImageFormatWriter<PcdFile> {

  /// <summary>Size of the preamble (zeros) before the magic identifier.</summary>
  internal const int PreambleSize = 2048;

  /// <summary>The magic identifier at offset 2048. The byte after it is a version, not a terminator.</summary>
  internal static readonly byte[] Magic = "PCD_IPI"u8.ToArray();

  /// <summary>The resolutions a Photo CD holds, smallest first, and where each starts.</summary>
  /// <remarks>
  /// A Photo CD is a pyramid of the same picture at fixed sizes, each at a fixed place. Nothing in
  /// the file states a size — the offset a plane starts at is what says which size it is.
  /// </remarks>
  internal static readonly (int Width, int Height, int Offset)[] Resolutions = [
    (192, 128, 8192),
    (384, 256, 47104),
    (768, 512, 196608),
  ];

  /// <summary>Bytes one resolution occupies: a luminance plane and two at half of it each way.</summary>
  internal static int PlaneBytes(int width, int height) => width * height * 3 / 2;

  static string IImageFormatMetadata<PcdFile>.PrimaryExtension => ".pcd";
  static string[] IImageFormatMetadata<PcdFile>.FileExtensions => [".pcd"];
  static PcdFile IImageFormatReader<PcdFile>.FromSpan(ReadOnlySpan<byte> data) => PcdReader.FromSpan(data);
  static byte[] IImageFormatWriter<PcdFile>.ToBytes(PcdFile file) => PcdWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PcdFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PcdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
