using System;
using FileFormat.Core;

namespace FileFormat.ZeissLsm;

/// <summary>In-memory representation of a Zeiss LSM confocal microscopy image (simplified TIFF-based).</summary>
public readonly record struct ZeissLsmFile : IImageFormatReader<ZeissLsmFile>, IImageToRawImage<ZeissLsmFile>, IImageFromRawImage<ZeissLsmFile>, IImageFormatWriter<ZeissLsmFile> {

  /// <summary>TIFF magic number for little-endian.</summary>
  internal const ushort TiffMagicLE = 42;

  /// <summary>TIFF byte order marker for little-endian (II).</summary>
  internal const ushort ByteOrderLE = 0x4949;

  static string IImageFormatMetadata<ZeissLsmFile>.PrimaryExtension => ".lsm";
  static string[] IImageFormatMetadata<ZeissLsmFile>.FileExtensions => [".lsm"];
  static ZeissLsmFile IImageFormatReader<ZeissLsmFile>.FromSpan(ReadOnlySpan<byte> data) => ZeissLsmReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZeissLsmFile>.ToBytes(ZeissLsmFile file) => ZeissLsmWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of channels (1 = grayscale, 3+ = color).</summary>
  public int Channels { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts to Gray8 (1 channel) or Rgb24 (3+ channels).</summary>
  public static RawImage ToRawImage(ZeissLsmFile file) {

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
