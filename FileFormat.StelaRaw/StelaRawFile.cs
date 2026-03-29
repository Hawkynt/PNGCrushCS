using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.StelaRaw;

/// <summary>In-memory representation of a Stela/HSI Raw image image.</summary>
public sealed class StelaRawFile : IImageFileFormat<StelaRawFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<StelaRawFile>.PrimaryExtension => ".hsi";
  static string[] IImageFileFormat<StelaRawFile>.FileExtensions => [".hsi"];
  static StelaRawFile IImageFileFormat<StelaRawFile>.FromFile(FileInfo file) => StelaRawReader.FromFile(file);
  static StelaRawFile IImageFileFormat<StelaRawFile>.FromBytes(byte[] data) => StelaRawReader.FromBytes(data);
  static StelaRawFile IImageFileFormat<StelaRawFile>.FromStream(Stream stream) => StelaRawReader.FromStream(stream);
  static byte[] IImageFileFormat<StelaRawFile>.ToBytes(StelaRawFile file) => StelaRawWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(StelaRawFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
