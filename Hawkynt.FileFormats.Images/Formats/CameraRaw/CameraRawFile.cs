using System;
using FileFormat.Core;

namespace FileFormat.CameraRaw;

/// <summary>In-memory representation of a Camera RAW image (CR2/NEF/ARW/ORF/RW2/PEF/RAF). Stores the embedded preview image as RGB24 pixel data.</summary>
public readonly record struct CameraRawFile : IImageFormatReader<CameraRawFile>, IImageToRawImage<CameraRawFile>, IImageFromRawImage<CameraRawFile>, IImageFormatWriter<CameraRawFile> {

  static string IImageFormatMetadata<CameraRawFile>.PrimaryExtension => ".cr2";
  /// <summary>
  /// The names raw files come under. All but the Fujifilm one are TIFF containers, so a file is
  /// readable here whether or not its sensor data is in a compression this understands — the
  /// preview inside it is a picture either way.
  /// </summary>
  static string[] IImageFormatMetadata<CameraRawFile>.FileExtensions => [
    ".cr2", ".nef", ".arw", ".orf", ".rw2", ".pef", ".raf", ".raw", ".srw", ".dcs",
    ".dcr", ".kdc", ".srf", ".sr2", ".mos", ".3fr", ".mef", ".nrw", ".rwl", ".erf", ".iiq",
  ];
  static CameraRawFile IImageFormatReader<CameraRawFile>.FromSpan(ReadOnlySpan<byte> data) => CameraRawReader.FromSpan(data);
  static byte[] IImageFormatWriter<CameraRawFile>.ToBytes(CameraRawFile file) => CameraRawWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The identified camera manufacturer.</summary>
  public CameraRawManufacturer Manufacturer { get; init; }

  /// <summary>The camera model string extracted from TIFF tags, if available.</summary>
  public string Model { get; init; }

  public static RawImage ToRawImage(CameraRawFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CameraRawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Manufacturer = CameraRawManufacturer.Generic,
    };
  }
}
