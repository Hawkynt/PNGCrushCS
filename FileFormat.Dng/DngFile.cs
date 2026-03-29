using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Dng;

/// <summary>In-memory representation of a DNG (Adobe Digital Negative) image.</summary>
public sealed class DngFile : IImageFileFormat<DngFile> {

  static string IImageFileFormat<DngFile>.PrimaryExtension => ".dng";
  static string[] IImageFileFormat<DngFile>.FileExtensions => [".dng"];
  static DngFile IImageFileFormat<DngFile>.FromFile(FileInfo file) => DngReader.FromFile(file);
  static DngFile IImageFileFormat<DngFile>.FromBytes(byte[] data) => DngReader.FromBytes(data);
  static DngFile IImageFileFormat<DngFile>.FromStream(Stream stream) => DngReader.FromStream(stream);
  static byte[] IImageFileFormat<DngFile>.ToBytes(DngFile file) => DngWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per sample (typically 8 or 16).</summary>
  public int BitsPerSample { get; init; } = 8;

  /// <summary>Number of samples (channels) per pixel (1=Gray, 3=RGB).</summary>
  public int SamplesPerPixel { get; init; } = 3;

  /// <summary>Raw pixel data in interleaved channel order.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>DNG version bytes, e.g. [1,4,0,0].</summary>
  public byte[] DngVersion { get; init; } = [1, 4, 0, 0];

  /// <summary>Unique camera model string.</summary>
  public string CameraModel { get; init; } = "";

  /// <summary>Photometric interpretation of the image data.</summary>
  public DngPhotometric Photometric { get; init; }

  public static RawImage ToRawImage(DngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var format = file.SamplesPerPixel switch {
      1 when file.BitsPerSample == 8 => PixelFormat.Gray8,
      3 when file.BitsPerSample == 8 => PixelFormat.Rgb24,
      _ => throw new NotSupportedException($"DNG with {file.SamplesPerPixel} samples and {file.BitsPerSample} bits/sample is not supported for RawImage conversion.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static DngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var (samplesPerPixel, bitsPerSample, photometric) = image.Format switch {
      PixelFormat.Gray8 => (1, 8, DngPhotometric.BlackIsZero),
      PixelFormat.Rgb24 => (3, 8, DngPhotometric.Rgb),
      _ => throw new ArgumentException($"Pixel format {image.Format} is not supported by DNG.", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      Photometric = photometric,
      PixelData = image.PixelData[..],
    };
  }
}
