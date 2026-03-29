using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZeissBivas;

/// <summary>In-memory representation of a Zeiss BIVAS microscopy raw data image.</summary>
public sealed class ZeissBivasFile : IImageFileFormat<ZeissBivasFile> {

  /// <summary>Header size: uint32 width + uint32 height + uint32 bpp = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  static string IImageFileFormat<ZeissBivasFile>.PrimaryExtension => ".dta";
  static string[] IImageFileFormat<ZeissBivasFile>.FileExtensions => [".dta"];
  static ZeissBivasFile IImageFileFormat<ZeissBivasFile>.FromFile(FileInfo file) => ZeissBivasReader.FromFile(file);
  static ZeissBivasFile IImageFileFormat<ZeissBivasFile>.FromBytes(byte[] data) => ZeissBivasReader.FromBytes(data);
  static ZeissBivasFile IImageFileFormat<ZeissBivasFile>.FromStream(Stream stream) => ZeissBivasReader.FromStream(stream);
  static RawImage IImageFileFormat<ZeissBivasFile>.ToRawImage(ZeissBivasFile file) => ToRawImage(file);
  static ZeissBivasFile IImageFileFormat<ZeissBivasFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<ZeissBivasFile>.ToBytes(ZeissBivasFile file) => ZeissBivasWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (typically 8).</summary>
  public int BitsPerPixel { get; init; } = 8;

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts to Gray8.</summary>
  public static RawImage ToRawImage(ZeissBivasFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedSize = file.Width * file.Height;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = pixelData,
    };
  }

  /// <summary>Creates a Zeiss BIVAS image from a Gray8 raw image.</summary>
  public static ZeissBivasFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    var pixelData = image.PixelData[..];

    return new() { Width = image.Width, Height = image.Height, BitsPerPixel = 8, PixelData = pixelData };
  }
}
