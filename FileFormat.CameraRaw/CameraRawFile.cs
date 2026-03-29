using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CameraRaw;

/// <summary>In-memory representation of a Camera RAW image (CR2/NEF/ARW/ORF/RW2/PEF/RAF). Stores the embedded preview image as RGB24 pixel data.</summary>
public sealed class CameraRawFile : IImageFileFormat<CameraRawFile> {

  static string IImageFileFormat<CameraRawFile>.PrimaryExtension => ".cr2";
  static string[] IImageFileFormat<CameraRawFile>.FileExtensions => [".cr2", ".nef", ".arw", ".orf", ".rw2", ".pef", ".raf", ".raw", ".srw", ".dcs"];
  static CameraRawFile IImageFileFormat<CameraRawFile>.FromFile(FileInfo file) => CameraRawReader.FromFile(file);
  static CameraRawFile IImageFileFormat<CameraRawFile>.FromBytes(byte[] data) => CameraRawReader.FromBytes(data);
  static CameraRawFile IImageFileFormat<CameraRawFile>.FromStream(Stream stream) => CameraRawReader.FromStream(stream);
  static byte[] IImageFileFormat<CameraRawFile>.ToBytes(CameraRawFile file) => CameraRawWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>The identified camera manufacturer.</summary>
  public CameraRawManufacturer Manufacturer { get; init; }

  /// <summary>The camera model string extracted from TIFF tags, if available.</summary>
  public string Model { get; init; } = "";

  public static RawImage ToRawImage(CameraRawFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CameraRawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Pixel format {image.Format} is not supported by Camera RAW. Only Rgb24 is accepted.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Manufacturer = CameraRawManufacturer.Generic,
    };
  }
}
