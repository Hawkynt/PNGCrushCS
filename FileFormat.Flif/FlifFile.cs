using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Flif;

/// <summary>In-memory representation of a FLIF (Free Lossless Image Format) image.</summary>
[FormatMagicBytes([0x46, 0x4C, 0x49, 0x46])]
public sealed class FlifFile : IImageFileFormat<FlifFile> {

  static string IImageFileFormat<FlifFile>.PrimaryExtension => ".flif";
  static string[] IImageFileFormat<FlifFile>.FileExtensions => [".flif"];
  static FlifFile IImageFileFormat<FlifFile>.FromFile(FileInfo file) => FlifReader.FromFile(file);
  static FlifFile IImageFileFormat<FlifFile>.FromBytes(byte[] data) => FlifReader.FromBytes(data);
  static FlifFile IImageFileFormat<FlifFile>.FromStream(Stream stream) => FlifReader.FromStream(stream);
  static byte[] IImageFileFormat<FlifFile>.ToBytes(FlifFile file) => FlifWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of channels (1=Gray, 3=RGB, 4=RGBA).</summary>
  public FlifChannelCount ChannelCount { get; init; } = FlifChannelCount.Rgb;

  /// <summary>Bits per channel (8 or 16).</summary>
  public int BitsPerChannel { get; init; } = 8;

  /// <summary>Whether the image uses interlacing.</summary>
  public bool IsInterlaced { get; init; }

  /// <summary>Whether the image is animated.</summary>
  public bool IsAnimated { get; init; }

  /// <summary>Raw pixel data in channel order (Gray8, Rgb24, or Rgba32 depending on channel count for 8-bit).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(FlifFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var format = file.ChannelCount switch {
      FlifChannelCount.Gray => PixelFormat.Gray8,
      FlifChannelCount.Rgb => PixelFormat.Rgb24,
      FlifChannelCount.Rgba => PixelFormat.Rgba32,
      _ => throw new InvalidOperationException($"Unsupported channel count for raw image conversion: {file.ChannelCount}.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static FlifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ChannelCount = FlifChannelCount.Gray,
          BitsPerChannel = 8,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ChannelCount = FlifChannelCount.Rgb,
          BitsPerChannel = 8,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ChannelCount = FlifChannelCount.Rgba,
          BitsPerChannel = 8,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for FLIF: {image.Format}", nameof(image));
    }
  }
}
