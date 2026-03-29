using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.XvThumbnail;

/// <summary>In-memory representation of an XV thumbnail image (P7 332 format).</summary>
[FormatDetectionPriority(90)]
[FormatMagicBytes([0x50, 0x37, 0x20, 0x33, 0x33, 0x32])]
public sealed class XvThumbnailFile : IImageFileFormat<XvThumbnailFile> {

  static string IImageFileFormat<XvThumbnailFile>.PrimaryExtension => ".xv";
  static string[] IImageFileFormat<XvThumbnailFile>.FileExtensions => [".xv"];
  static XvThumbnailFile IImageFileFormat<XvThumbnailFile>.FromFile(FileInfo file) => XvThumbnailReader.FromFile(file);
  static XvThumbnailFile IImageFileFormat<XvThumbnailFile>.FromBytes(byte[] data) => XvThumbnailReader.FromBytes(data);
  static XvThumbnailFile IImageFileFormat<XvThumbnailFile>.FromStream(Stream stream) => XvThumbnailReader.FromStream(stream);
  static byte[] IImageFileFormat<XvThumbnailFile>.ToBytes(XvThumbnailFile file) => XvThumbnailWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw 3-3-2 packed pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts an XV thumbnail to a platform-independent RGB24 image.</summary>
  public static RawImage ToRawImage(XvThumbnailFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var packed = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      var r = (packed >> 5) & 0x07;
      var g = (packed >> 2) & 0x07;
      var b = packed & 0x03;
      rgb[i * 3] = (byte)(r * 255 / 7);
      rgb[i * 3 + 1] = (byte)(g * 255 / 7);
      rgb[i * 3 + 2] = (byte)(b * 255 / 3);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an XV thumbnail from a platform-independent RGB24 image.</summary>
  public static XvThumbnailFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var packed = new byte[pixelCount];

    for (var i = 0; i < pixelCount; ++i) {
      var r = image.PixelData[i * 3];
      var g = image.PixelData[i * 3 + 1];
      var b = image.PixelData[i * 3 + 2];
      var r3 = (r * 7 + 127) / 255;
      var g3 = (g * 7 + 127) / 255;
      var b2 = (b * 3 + 127) / 255;
      packed[i] = (byte)((r3 << 5) | (g3 << 2) | b2);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = packed,
    };
  }
}
