using System;
using FileFormat.Core;

namespace FileFormat.TiBitmap;

/// <summary>In-memory representation of a TI Calculator bitmap image.</summary>
public readonly record struct TiBitmapFile : IImageFormatReader<TiBitmapFile>, IImageToRawImage<TiBitmapFile>, IImageFromRawImage<TiBitmapFile>, IImageFormatWriter<TiBitmapFile> {

  internal const int HeaderSize = 8;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<TiBitmapFile>.PrimaryExtension => ".8xi";
  static string[] IImageFormatMetadata<TiBitmapFile>.FileExtensions => [".8xi", ".89i"];
  static TiBitmapFile IImageFormatReader<TiBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => TiBitmapReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<TiBitmapFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<TiBitmapFile>.ToBytes(TiBitmapFile file) => TiBitmapWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(TiBitmapFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static TiBitmapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
