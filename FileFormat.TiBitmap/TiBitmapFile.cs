using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TiBitmap;

/// <summary>In-memory representation of a TI Calculator bitmap image.</summary>
public sealed class TiBitmapFile : IImageFileFormat<TiBitmapFile> {

  internal const int HeaderSize = 8;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<TiBitmapFile>.PrimaryExtension => ".8xi";
  static string[] IImageFileFormat<TiBitmapFile>.FileExtensions => [".8xi", ".89i"];
  static FormatCapability IImageFileFormat<TiBitmapFile>.Capabilities => FormatCapability.MonochromeOnly;
  static TiBitmapFile IImageFileFormat<TiBitmapFile>.FromFile(FileInfo file) => TiBitmapReader.FromFile(file);
  static TiBitmapFile IImageFileFormat<TiBitmapFile>.FromBytes(byte[] data) => TiBitmapReader.FromBytes(data);
  static TiBitmapFile IImageFileFormat<TiBitmapFile>.FromStream(Stream stream) => TiBitmapReader.FromStream(stream);
  static byte[] IImageFileFormat<TiBitmapFile>.ToBytes(TiBitmapFile file) => TiBitmapWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(TiBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
