using System;
using FileFormat.Core;

namespace FileFormat.JpegXr;

/// <summary>In-memory representation of a JPEG XR (ITU-T T.832 / ISO 29199-2) image.</summary>
public readonly record struct JpegXrFile : IImageFormatReader<JpegXrFile>, IImageToRawImage<JpegXrFile>, IImageFromRawImage<JpegXrFile>, IImageFormatWriter<JpegXrFile> {

  static string IImageFormatMetadata<JpegXrFile>.PrimaryExtension => ".jxr";
  static string[] IImageFormatMetadata<JpegXrFile>.FileExtensions => [".jxr", ".wdp", ".hdp"];
  static JpegXrFile IImageFormatReader<JpegXrFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXrReader.FromSpan(data);

  static bool? IImageFormatMetadata<JpegXrFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x01 && header[3] == 0xBC
      ? true : null;

  static byte[] IImageFormatWriter<JpegXrFile>.ToBytes(JpegXrFile file) => JpegXrWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of components per pixel (1=Grayscale, 3=RGB).</summary>
  public int ComponentCount { get; init; }

  /// <summary>Raw pixel data: Gray8 (1 byte/pixel) or Rgb24 (3 bytes/pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(JpegXrFile file) {
    var format = file.ComponentCount switch {
      1 => PixelFormat.Gray8,
      3 => PixelFormat.Rgb24,
      _ => throw new NotSupportedException($"JPEG XR with {file.ComponentCount} components is not supported.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static JpegXrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var componentCount = image.Format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Rgb24 => 3,
      _ => throw new ArgumentException($"Pixel format {image.Format} is not supported by JPEG XR.", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
    };
  }
}
