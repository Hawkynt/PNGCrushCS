using System;
using FileFormat.Core;

namespace FileFormat.StelaRaw;

/// <summary>In-memory representation of a Stela/HSI Raw image image.</summary>
public readonly record struct StelaRawFile : IImageFormatReader<StelaRawFile>, IImageToRawImage<StelaRawFile>, IImageFromRawImage<StelaRawFile>, IImageFormatWriter<StelaRawFile> {

  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<StelaRawFile>.PrimaryExtension => ".hsi";
  static string[] IImageFormatMetadata<StelaRawFile>.FileExtensions => [".hsi"];
  static StelaRawFile IImageFormatReader<StelaRawFile>.FromSpan(ReadOnlySpan<byte> data) => StelaRawReader.FromSpan(data);
  static byte[] IImageFormatWriter<StelaRawFile>.ToBytes(StelaRawFile file) => StelaRawWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(StelaRawFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static StelaRawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
