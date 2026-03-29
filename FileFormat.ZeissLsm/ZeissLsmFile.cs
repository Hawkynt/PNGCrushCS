using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZeissLsm;

/// <summary>In-memory representation of a Zeiss LSM confocal microscopy image (simplified TIFF-based).</summary>
public sealed class ZeissLsmFile : IImageFileFormat<ZeissLsmFile> {

  /// <summary>TIFF magic number for little-endian.</summary>
  internal const ushort TiffMagicLE = 42;

  /// <summary>TIFF byte order marker for little-endian (II).</summary>
  internal const ushort ByteOrderLE = 0x4949;

  static string IImageFileFormat<ZeissLsmFile>.PrimaryExtension => ".lsm";
  static string[] IImageFileFormat<ZeissLsmFile>.FileExtensions => [".lsm"];
  static ZeissLsmFile IImageFileFormat<ZeissLsmFile>.FromFile(FileInfo file) => ZeissLsmReader.FromFile(file);
  static ZeissLsmFile IImageFileFormat<ZeissLsmFile>.FromBytes(byte[] data) => ZeissLsmReader.FromBytes(data);
  static ZeissLsmFile IImageFileFormat<ZeissLsmFile>.FromStream(Stream stream) => ZeissLsmReader.FromStream(stream);
  static RawImage IImageFileFormat<ZeissLsmFile>.ToRawImage(ZeissLsmFile file) => ToRawImage(file);
  static ZeissLsmFile IImageFileFormat<ZeissLsmFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<ZeissLsmFile>.ToBytes(ZeissLsmFile file) => ZeissLsmWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of channels (1 = grayscale, 3+ = color).</summary>
  public int Channels { get; init; } = 1;

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts to Gray8 (1 channel) or Rgb24 (3+ channels).</summary>
  public static RawImage ToRawImage(ZeissLsmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Channels >= 3) {
      var expectedSize = file.Width * file.Height * 3;
      var pixelData = new byte[expectedSize];
      file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = pixelData,
      };
    }

    var graySize = file.Width * file.Height;
    var grayData = new byte[graySize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, graySize)).CopyTo(grayData);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = grayData,
    };
  }

  /// <summary>Creates a ZeissLsm file from a Gray8 or Rgb24 raw image.</summary>
  public static ZeissLsmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8 && image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var channels = image.Format == PixelFormat.Rgb24 ? 3 : 1;
    var pixelData = image.PixelData[..];

    return new() { Width = image.Width, Height = image.Height, Channels = channels, PixelData = pixelData };
  }
}
