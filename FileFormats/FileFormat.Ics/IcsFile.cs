using System;
using FileFormat.Core;

namespace FileFormat.Ics;

/// <summary>In-memory representation of an ICS (Image Cytometry Standard) image.</summary>
public readonly record struct IcsFile : IImageFormatReader<IcsFile>, IImageToRawImage<IcsFile>, IImageFromRawImage<IcsFile>, IImageFormatWriter<IcsFile> {

  static string IImageFormatMetadata<IcsFile>.PrimaryExtension => ".ics";
  static string[] IImageFormatMetadata<IcsFile>.FileExtensions => [".ics"];
  static IcsFile IImageFormatReader<IcsFile>.FromSpan(ReadOnlySpan<byte> data) => IcsReader.FromSpan(data);
  static byte[] IImageFormatWriter<IcsFile>.ToBytes(IcsFile file) => IcsWriter.ToBytes(file);

  /// <summary>ICS version ("1.0" or "2.0").</summary>
  public string Version { get; init; }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of channels (1 for grayscale, 3 for RGB).</summary>
  public int Channels { get; init; }

  /// <summary>Bits per sample (e.g. 8).</summary>
  public int BitsPerSample { get; init; }

  /// <summary>Whether pixel data is compressed.</summary>
  public bool IsCompressed => Compression != IcsCompression.Uncompressed;

  /// <summary>Compression method.</summary>
  public IcsCompression Compression { get; init; }

  /// <summary>Raw pixel data (interleaved channel order: for 3-channel, R G B R G B ...).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IcsFile file) {

    var format = file.Channels switch {
      1 => PixelFormat.Gray8,
      3 => PixelFormat.Rgb24,
      _ => throw new InvalidOperationException($"Unsupported channel count for raw image conversion: {file.Channels}.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static IcsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Channels = 1,
          BitsPerSample = 8,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Channels = 3,
          BitsPerSample = 8,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for ICS: {image.Format}", nameof(image));
    }
  }
}
