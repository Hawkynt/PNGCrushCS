using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Rembrandt;

/// <summary>In-memory representation of an Atari Falcon Rembrandt (.tcp) true-color image.</summary>
public sealed class RembrandtFile : IImageFileFormat<RembrandtFile> {

  /// <summary>Size of the dimension header (width BE u16 + height BE u16).</summary>
  public const int HeaderSize = 4;

  /// <summary>Minimum valid file size (header + at least 1 pixel x 2 bytes).</summary>
  public const int MinFileSize = HeaderSize + 2;

  static string IImageFileFormat<RembrandtFile>.PrimaryExtension => ".tcp";
  static string[] IImageFileFormat<RembrandtFile>.FileExtensions => [".tcp"];
  static RembrandtFile IImageFileFormat<RembrandtFile>.FromFile(FileInfo file) => RembrandtReader.FromFile(file);
  static RembrandtFile IImageFileFormat<RembrandtFile>.FromBytes(byte[] data) => RembrandtReader.FromBytes(data);
  static RembrandtFile IImageFileFormat<RembrandtFile>.FromStream(Stream stream) => RembrandtReader.FromStream(stream);
  static byte[] IImageFileFormat<RembrandtFile>.ToBytes(RembrandtFile file) => RembrandtWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(RembrandtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgb24 = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var hi = srcOffset < file.PixelData.Length ? file.PixelData[srcOffset] : (byte)0;
      var lo = srcOffset + 1 < file.PixelData.Length ? file.PixelData[srcOffset + 1] : (byte)0;
      var packed = (ushort)((hi << 8) | lo);

      var r5 = (packed >> 11) & 0x1F;
      var g6 = (packed >> 5) & 0x3F;
      var b5 = packed & 0x1F;

      var dstOffset = i * 3;
      rgb24[dstOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[dstOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[dstOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static RembrandtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException($"Dimensions must be positive, got {image.Width}x{image.Height}.", nameof(image));
    if (image.Width > 65535 || image.Height > 65535)
      throw new ArgumentException($"Dimensions must fit in a ushort, got {image.Width}x{image.Height}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var rgb565 = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 3;
      var r = image.PixelData[srcOffset];
      var g = image.PixelData[srcOffset + 1];
      var b = image.PixelData[srcOffset + 2];

      var r5 = (r >> 3) & 0x1F;
      var g6 = (g >> 2) & 0x3F;
      var b5 = (b >> 3) & 0x1F;
      var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);

      var dstOffset = i * 2;
      rgb565[dstOffset] = (byte)(packed >> 8);
      rgb565[dstOffset + 1] = (byte)(packed & 0xFF);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = rgb565,
    };
  }
}
