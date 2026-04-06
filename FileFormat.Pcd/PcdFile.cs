using System;
using FileFormat.Core;

namespace FileFormat.Pcd;

/// <summary>In-memory representation of a PCD (Kodak Photo CD) image.</summary>
public readonly record struct PcdFile : IImageFormatReader<PcdFile>, IImageToRawImage<PcdFile>, IImageFromRawImage<PcdFile>, IImageFormatWriter<PcdFile> {

  /// <summary>Size of the preamble (zeros) before the magic identifier.</summary>
  internal const int PreambleSize = 2048;

  /// <summary>The magic identifier at offset 2048.</summary>
  internal static readonly byte[] Magic = "PCD_IPI\0"u8.ToArray();

  /// <summary>Total header size: preamble + magic + 2 x uint16 dimensions.</summary>
  internal const int HeaderSize = PreambleSize + 8 + 4;

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
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
