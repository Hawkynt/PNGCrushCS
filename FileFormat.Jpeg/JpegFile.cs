using System;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>In-memory representation of a JPEG image.</summary>
public readonly record struct JpegFile : IImageFormatReader<JpegFile>, IImageToRawImage<JpegFile>, IImageFromRawImage<JpegFile>, IImageFormatWriter<JpegFile> {

  static string IImageFormatMetadata<JpegFile>.PrimaryExtension => ".jpg";
  static string[] IImageFormatMetadata<JpegFile>.FileExtensions => [".jpg", ".jpeg", ".jpe", ".jfif", ".jps", ".thm"];
  static JpegFile IImageFormatReader<JpegFile>.FromSpan(ReadOnlySpan<byte> data) => JpegReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<JpegFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;

  static bool? IImageFormatMetadata<JpegFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF
      ? true : null;

  static byte[] IImageFormatWriter<JpegFile>.ToBytes(JpegFile file) => JpegWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public bool IsGrayscale { get; init; }
  public byte[]? RgbPixelData { get; init; }
  public byte[]? RawJpegBytes { get; init; }

  public static RawImage ToRawImage(JpegFile file) {
    if (file.RgbPixelData == null)
      throw new ArgumentException("RgbPixelData must not be null. Ensure the JPEG was decoded before conversion.", nameof(file));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.IsGrayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.RgbPixelData[..],
    };
  }

  public static JpegFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    bool isGrayscale;
    if (image.Format == PixelFormat.Gray8)
      isGrayscale = true;
    else if (image.Format == PixelFormat.Rgb24)
      isGrayscale = false;
    else
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} or {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      IsGrayscale = isGrayscale,
      RgbPixelData = image.PixelData[..],
    };
  }
}
