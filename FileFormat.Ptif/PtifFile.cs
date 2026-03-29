using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ptif;

/// <summary>In-memory representation of a PTIF (Pyramid TIFF) image. Only the first (full-resolution) IFD is used.</summary>
public sealed class PtifFile : IImageFileFormat<PtifFile> {

  static string IImageFileFormat<PtifFile>.PrimaryExtension => ".ptif";
  static string[] IImageFileFormat<PtifFile>.FileExtensions => [".ptif", ".ptiff"];
  static PtifFile IImageFileFormat<PtifFile>.FromFile(FileInfo file) => PtifReader.FromFile(file);
  static PtifFile IImageFileFormat<PtifFile>.FromBytes(byte[] data) => PtifReader.FromBytes(data);
  static PtifFile IImageFileFormat<PtifFile>.FromStream(Stream stream) => PtifReader.FromStream(stream);
  static byte[] IImageFileFormat<PtifFile>.ToBytes(PtifFile file) => PtifWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of samples (channels) per pixel (1=Gray, 3=RGB, 4=RGBA).</summary>
  public int SamplesPerPixel { get; init; }

  /// <summary>Bits per sample (currently only 8 is supported).</summary>
  public int BitsPerSample { get; init; }

  /// <summary>Raw pixel data in interleaved channel order.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PtifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var format = file.SamplesPerPixel switch {
      1 when file.BitsPerSample == 8 => PixelFormat.Gray8,
      3 when file.BitsPerSample == 8 => PixelFormat.Rgb24,
      4 when file.BitsPerSample == 8 => PixelFormat.Rgba32,
      _ => throw new NotSupportedException($"PTIF with {file.SamplesPerPixel} samples and {file.BitsPerSample} bits/sample is not supported.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static PtifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var (samplesPerPixel, bitsPerSample) = image.Format switch {
      PixelFormat.Gray8 => (1, 8),
      PixelFormat.Rgb24 => (3, 8),
      PixelFormat.Rgba32 => (4, 8),
      _ => throw new ArgumentException($"Pixel format {image.Format} is not supported by PTIF.", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = image.PixelData[..],
    };
  }
}
