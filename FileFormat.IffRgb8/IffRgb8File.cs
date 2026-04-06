using System;
using FileFormat.Core;

namespace FileFormat.IffRgb8;

/// <summary>In-memory representation of an IFF RGB8 (24-bit RGB) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public readonly record struct IffRgb8File : IImageFormatReader<IffRgb8File>, IImageToRawImage<IffRgb8File>, IImageFromRawImage<IffRgb8File>, IImageFormatWriter<IffRgb8File> {

  static string IImageFormatMetadata<IffRgb8File>.PrimaryExtension => ".rgb8";
  static string[] IImageFormatMetadata<IffRgb8File>.FileExtensions => [".rgb8", ".iff"];
  static IffRgb8File IImageFormatReader<IffRgb8File>.FromSpan(ReadOnlySpan<byte> data) => IffRgb8Reader.FromSpan(data);

  static bool? IImageFormatMetadata<IffRgb8File>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x52 && header[9] == 0x47 && header[10] == 0x42 && header[11] == 0x38;

  static byte[] IImageFormatWriter<IffRgb8File>.ToBytes(IffRgb8File file) => IffRgb8Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Compression method used for the BODY chunk.</summary>
  public IffRgb8Compression Compression { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel: R, G, B).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this IFF RGB8 file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffRgb8File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an <see cref="IffRgb8File"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IffRgb8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = image.PixelData[..],
    };
  }
}
