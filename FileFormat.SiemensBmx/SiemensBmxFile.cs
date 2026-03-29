using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SiemensBmx;

/// <summary>In-memory representation of a Siemens mobile bitmap image.</summary>
public sealed class SiemensBmxFile : IImageFileFormat<SiemensBmxFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<SiemensBmxFile>.PrimaryExtension => ".bmx";
  static string[] IImageFileFormat<SiemensBmxFile>.FileExtensions => [".bmx"];
  static FormatCapability IImageFileFormat<SiemensBmxFile>.Capabilities => FormatCapability.IndexedOnly;
  static SiemensBmxFile IImageFileFormat<SiemensBmxFile>.FromFile(FileInfo file) => SiemensBmxReader.FromFile(file);
  static SiemensBmxFile IImageFileFormat<SiemensBmxFile>.FromBytes(byte[] data) => SiemensBmxReader.FromBytes(data);
  static SiemensBmxFile IImageFileFormat<SiemensBmxFile>.FromStream(Stream stream) => SiemensBmxReader.FromStream(stream);
  static byte[] IImageFileFormat<SiemensBmxFile>.ToBytes(SiemensBmxFile file) => SiemensBmxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(SiemensBmxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static SiemensBmxFile FromRawImage(RawImage image) {
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
