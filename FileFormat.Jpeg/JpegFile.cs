using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>In-memory representation of a JPEG image.</summary>
public sealed class JpegFile : IImageFileFormat<JpegFile> {

  static string IImageFileFormat<JpegFile>.PrimaryExtension => ".jpg";
  static string[] IImageFileFormat<JpegFile>.FileExtensions => [".jpg", ".jpeg", ".jpe", ".jfif"];
  static JpegFile IImageFileFormat<JpegFile>.FromFile(FileInfo file) => JpegReader.FromFile(file);
  static byte[] IImageFileFormat<JpegFile>.ToBytes(JpegFile file) => JpegWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public bool IsGrayscale { get; init; }
  public byte[]? RgbPixelData { get; init; }
  public byte[]? RawJpegBytes { get; init; }

  public static RawImage ToRawImage(JpegFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.RgbPixelData == null)
      throw new ArgumentException("RgbPixelData must not be null. Ensure the JPEG was decoded before conversion.", nameof(file));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.IsGrayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = (byte[])file.RgbPixelData.Clone(),
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
      RgbPixelData = (byte[])image.PixelData.Clone(),
    };
  }
}
