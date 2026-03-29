using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Interfile;

/// <summary>In-memory representation of an Interfile nuclear medicine image (SPECT/PET).</summary>
[FormatMagicBytes([0x21, 0x49, 0x4E, 0x54, 0x45, 0x52, 0x46, 0x49, 0x4C, 0x45])]
public sealed class InterfileFile : IImageFileFormat<InterfileFile> {

  static string IImageFileFormat<InterfileFile>.PrimaryExtension => ".hv";
  static string[] IImageFileFormat<InterfileFile>.FileExtensions => [".hv"];
  static InterfileFile IImageFileFormat<InterfileFile>.FromFile(FileInfo file) => InterfileReader.FromFile(file);
  static InterfileFile IImageFileFormat<InterfileFile>.FromBytes(byte[] data) => InterfileReader.FromBytes(data);
  static InterfileFile IImageFileFormat<InterfileFile>.FromStream(Stream stream) => InterfileReader.FromStream(stream);
  static byte[] IImageFileFormat<InterfileFile>.ToBytes(InterfileFile file) => InterfileWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bytes per pixel (1 for grayscale, 2 for 16-bit, 3 for RGB, 4 for 32-bit).</summary>
  public int BytesPerPixel { get; init; } = 1;

  /// <summary>Number format string (e.g. "unsigned integer", "signed integer").</summary>
  public string NumberFormat { get; init; } = "unsigned integer";

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(InterfileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    switch (file.BytesPerPixel) {
      case 1:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..],
        };
      case 2: {
        // 16-bit data stored as native LE; convert to Gray16 BE
        var pixelCount = file.Width * file.Height;
        var gray16 = new byte[pixelCount * 2];

        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          // LE (lo, hi) -> BE (hi, lo)
          gray16[i * 2] = file.PixelData[srcOffset + 1];
          gray16[i * 2 + 1] = file.PixelData[srcOffset];
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray16,
          PixelData = gray16,
        };
      }
      case 3:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = file.PixelData[..],
        };
      default:
        throw new InvalidOperationException($"Unsupported bytes per pixel for raw image conversion: {file.BytesPerPixel}.");
    }
  }

  public static InterfileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BytesPerPixel = 1,
          NumberFormat = "unsigned integer",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray16: {
        // Gray16 BE -> 16-bit LE
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 2];

        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          // BE (hi, lo) -> LE (lo, hi)
          pixelData[i * 2] = image.PixelData[srcOffset + 1];
          pixelData[i * 2 + 1] = image.PixelData[srcOffset];
        }

        return new() {
          Width = image.Width,
          Height = image.Height,
          BytesPerPixel = 2,
          NumberFormat = "unsigned integer",
          PixelData = pixelData,
        };
      }
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BytesPerPixel = 3,
          NumberFormat = "unsigned integer",
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for Interfile: {image.Format}", nameof(image));
    }
  }
}
