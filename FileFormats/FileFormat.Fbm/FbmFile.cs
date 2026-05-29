using System;
using FileFormat.Core;

namespace FileFormat.Fbm;

/// <summary>In-memory representation of a CMU Fuzzy Bitmap (FBM) image.</summary>
[FormatMagicBytes([0x25, 0x62, 0x69, 0x74, 0x6D, 0x61, 0x70, 0x00])]
public readonly record struct FbmFile : IImageFormatReader<FbmFile>, IImageToRawImage<FbmFile>, IImageFromRawImage<FbmFile>, IImageFormatWriter<FbmFile> {

  static string IImageFormatMetadata<FbmFile>.PrimaryExtension => ".fbm";
  static string[] IImageFormatMetadata<FbmFile>.FileExtensions => [".fbm"];
  static FbmFile IImageFormatReader<FbmFile>.FromSpan(ReadOnlySpan<byte> data) => FbmReader.FromSpan(data);
  static byte[] IImageFormatWriter<FbmFile>.ToBytes(FbmFile file) => FbmWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of bands: 1 for grayscale, 3 for RGB.</summary>
  public int Bands { get; init; }

  /// <summary>Raw pixel data (band-interleaved, no row padding). Length = Width * Height * Bands.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Optional title string from the header (up to 207 characters).</summary>
  public string Title { get; init; }

  public static RawImage ToRawImage(FbmFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.Bands == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static FbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format is not (PixelFormat.Gray8 or PixelFormat.Rgb24))
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bands = image.Format == PixelFormat.Gray8 ? 1 : 3,
      PixelData = image.PixelData[..],
    };
  }
}
