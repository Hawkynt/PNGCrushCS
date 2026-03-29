using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HereticM8;

/// <summary>In-memory representation of a Heretic II MipMap texture image.</summary>
public sealed class HereticM8File : IImageFileFormat<HereticM8File> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<HereticM8File>.PrimaryExtension => ".m8";
  static string[] IImageFileFormat<HereticM8File>.FileExtensions => [".m8"];
  static FormatCapability IImageFileFormat<HereticM8File>.Capabilities => FormatCapability.IndexedOnly;
  static HereticM8File IImageFileFormat<HereticM8File>.FromFile(FileInfo file) => HereticM8Reader.FromFile(file);
  static HereticM8File IImageFileFormat<HereticM8File>.FromBytes(byte[] data) => HereticM8Reader.FromBytes(data);
  static HereticM8File IImageFileFormat<HereticM8File>.FromStream(Stream stream) => HereticM8Reader.FromStream(stream);
  static byte[] IImageFileFormat<HereticM8File>.ToBytes(HereticM8File file) => HereticM8Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(HereticM8File file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static HereticM8File FromRawImage(RawImage image) {
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
