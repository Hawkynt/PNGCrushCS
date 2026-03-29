using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pi;

/// <summary>In-memory representation of a Pi format (NEC PC-88/98) image.</summary>
public sealed class PiFile : IImageFileFormat<PiFile> {

  internal const int HeaderSize = 18;


  static string IImageFileFormat<PiFile>.PrimaryExtension => ".pi";
  static string[] IImageFileFormat<PiFile>.FileExtensions => [".pi"];
  static FormatCapability IImageFileFormat<PiFile>.Capabilities => FormatCapability.IndexedOnly;
  static PiFile IImageFileFormat<PiFile>.FromFile(FileInfo file) => PiReader.FromFile(file);
  static PiFile IImageFileFormat<PiFile>.FromBytes(byte[] data) => PiReader.FromBytes(data);
  static PiFile IImageFileFormat<PiFile>.FromStream(Stream stream) => PiReader.FromStream(stream);
  static byte[] IImageFileFormat<PiFile>.ToBytes(PiFile file) => PiWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PiFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static PiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
