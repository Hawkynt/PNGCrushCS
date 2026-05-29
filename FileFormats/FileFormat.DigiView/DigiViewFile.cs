using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DigiView;

/// <summary>In-memory representation of a DigiView digitizer image.</summary>
public readonly record struct DigiViewFile : IImageFormatReader<DigiViewFile>, IImageToRawImage<DigiViewFile>, IImageFromRawImage<DigiViewFile>, IImageFormatWriter<DigiViewFile> {

  static string IImageFormatMetadata<DigiViewFile>.PrimaryExtension => ".dgv";
  static string[] IImageFormatMetadata<DigiViewFile>.FileExtensions => [".dgv"];
  static DigiViewFile IImageFormatReader<DigiViewFile>.FromSpan(ReadOnlySpan<byte> data) => DigiViewReader.FromSpan(data);
  static byte[] IImageFormatWriter<DigiViewFile>.ToBytes(DigiViewFile file) => DigiViewWriter.ToBytes(file);

  /// <summary>Size of the header in bytes (2 width BE + 2 height BE + 1 channels).</summary>
  internal const int HeaderSize = 5;

  /// <summary>Image width in pixels.</summary>
  public ushort Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public ushort Height { get; init; }

  /// <summary>Number of channels (1 = grayscale, 3 = RGB).</summary>
  public byte Channels { get; init; }

  /// <summary>Pixel data (width x height x channels bytes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this DigiView image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(DigiViewFile file) {

    return file.Channels switch {
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      },
      3 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      },
      _ => throw new InvalidDataException($"Unsupported DigiView channel count: {file.Channels}. Expected 1 (grayscale) or 3 (RGB).")
    };
  }

  /// <summary>Creates a DigiView image from a platform-independent <see cref="RawImage"/>. Accepts Gray8 or Rgb24 formats.</summary>
  public static DigiViewFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = (ushort)image.Width,
        Height = (ushort)image.Height,
        Channels = 1,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = (ushort)image.Width,
        Height = (ushort)image.Height,
        Channels = 3,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image))
    };
  }
}
