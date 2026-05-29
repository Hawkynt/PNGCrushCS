using System;
using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>In-memory representation of an EMF (Enhanced Metafile) image.</summary>
public readonly record struct EmfFile : IImageFormatReader<EmfFile>, IImageToRawImage<EmfFile>, IImageFromRawImage<EmfFile>, IImageFormatWriter<EmfFile> {

  static string IImageFormatMetadata<EmfFile>.PrimaryExtension => ".emf";
  static string[] IImageFormatMetadata<EmfFile>.FileExtensions => [".emf"];
  static EmfFile IImageFormatReader<EmfFile>.FromSpan(ReadOnlySpan<byte> data) => EmfReader.FromSpan(data);
  static byte[] IImageFormatWriter<EmfFile>.ToBytes(EmfFile file) => EmfWriter.ToBytes(file);

  static bool? IImageFormatMetadata<EmfFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 44 && header[0] == 0x01 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x00
      && header[40] == 0x20 && header[41] == 0x45 && header[42] == 0x4D && header[43] == 0x46
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel, top-down, no row padding).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(EmfFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static EmfFile FromRawImage(RawImage image) {
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
