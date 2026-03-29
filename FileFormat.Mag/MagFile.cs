using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mag;

/// <summary>In-memory representation of a MAKIchan Graphics image.</summary>
public sealed class MagFile : IImageFileFormat<MagFile> {

  internal const int HeaderSize = 32;


  static string IImageFileFormat<MagFile>.PrimaryExtension => ".mag";
  static string[] IImageFileFormat<MagFile>.FileExtensions => [".mag", ".mki"];
  static FormatCapability IImageFileFormat<MagFile>.Capabilities => FormatCapability.IndexedOnly;
  static MagFile IImageFileFormat<MagFile>.FromFile(FileInfo file) => MagReader.FromFile(file);
  static MagFile IImageFileFormat<MagFile>.FromBytes(byte[] data) => MagReader.FromBytes(data);
  static MagFile IImageFileFormat<MagFile>.FromStream(Stream stream) => MagReader.FromStream(stream);
  static byte[] IImageFileFormat<MagFile>.ToBytes(MagFile file) => MagWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MagFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static MagFile FromRawImage(RawImage image) {
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
