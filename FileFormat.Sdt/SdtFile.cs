using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Sdt;

/// <summary>In-memory representation of a SmartDraw thumbnail image.</summary>
public sealed class SdtFile : IImageFileFormat<SdtFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<SdtFile>.PrimaryExtension => ".sdt";
  static string[] IImageFileFormat<SdtFile>.FileExtensions => [".sdt"];
  static SdtFile IImageFileFormat<SdtFile>.FromFile(FileInfo file) => SdtReader.FromFile(file);
  static SdtFile IImageFileFormat<SdtFile>.FromBytes(byte[] data) => SdtReader.FromBytes(data);
  static SdtFile IImageFileFormat<SdtFile>.FromStream(Stream stream) => SdtReader.FromStream(stream);
  static byte[] IImageFileFormat<SdtFile>.ToBytes(SdtFile file) => SdtWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(SdtFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static SdtFile FromRawImage(RawImage image) {
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
