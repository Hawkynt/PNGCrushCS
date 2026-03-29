using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Farbfeld;

/// <summary>In-memory representation of a Farbfeld image.</summary>
[FormatMagicBytes([0x66, 0x61, 0x72, 0x62, 0x66, 0x65, 0x6C, 0x64])]
public sealed class FarbfeldFile : IImageFileFormat<FarbfeldFile> {

  static string IImageFileFormat<FarbfeldFile>.PrimaryExtension => ".ff";
  static string[] IImageFileFormat<FarbfeldFile>.FileExtensions => [".ff", ".farbfeld"];
  static FarbfeldFile IImageFileFormat<FarbfeldFile>.FromFile(FileInfo file) => FarbfeldReader.FromFile(file);
  static FarbfeldFile IImageFileFormat<FarbfeldFile>.FromBytes(byte[] data) => FarbfeldReader.FromBytes(data);
  static FarbfeldFile IImageFileFormat<FarbfeldFile>.FromStream(Stream stream) => FarbfeldReader.FromStream(stream);
  static byte[] IImageFileFormat<FarbfeldFile>.ToBytes(FarbfeldFile file) => FarbfeldWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGBA16 pixel data in big-endian byte order (8 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(FarbfeldFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba64,
      PixelData = file.PixelData[..],
    };
  }

  public static FarbfeldFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba64)
      throw new ArgumentException($"Expected {PixelFormat.Rgba64} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
