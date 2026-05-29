using System;
using FileFormat.Core;

namespace FileFormat.Pdn;

/// <summary>In-memory representation of a PDN (Paint.NET) image.</summary>
[FormatMagicBytes([0x50, 0x44, 0x4E, 0x33])]
public readonly record struct PdnFile : IImageFormatReader<PdnFile>, IImageToRawImage<PdnFile>, IImageFromRawImage<PdnFile>, IImageFormatWriter<PdnFile> {

  static string IImageFormatMetadata<PdnFile>.PrimaryExtension => ".pdn";
  static string[] IImageFormatMetadata<PdnFile>.FileExtensions => [".pdn"];
  static PdnFile IImageFormatReader<PdnFile>.FromSpan(ReadOnlySpan<byte> data) => PdnReader.FromSpan(data);
  static byte[] IImageFormatWriter<PdnFile>.ToBytes(PdnFile file) => PdnWriter.ToBytes(file);

  /// <summary>Format version (default 3).</summary>
  public ushort Version { get; init; }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw BGRA32 pixel data (4 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PdnFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Bgra32,
      PixelData = file.PixelData[..],
    };
  }

  public static PdnFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Bgra32)
      throw new ArgumentException($"Expected {PixelFormat.Bgra32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
