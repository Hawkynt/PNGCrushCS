using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.QuakeLmp;

/// <summary>In-memory representation of a Quake LMP picture lump image.</summary>
public sealed class QuakeLmpFile : IImageFileFormat<QuakeLmpFile> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<QuakeLmpFile>.PrimaryExtension => ".lmp";
  static string[] IImageFileFormat<QuakeLmpFile>.FileExtensions => [".lmp"];
  static FormatCapability IImageFileFormat<QuakeLmpFile>.Capabilities => FormatCapability.IndexedOnly;
  static QuakeLmpFile IImageFileFormat<QuakeLmpFile>.FromFile(FileInfo file) => QuakeLmpReader.FromFile(file);
  static QuakeLmpFile IImageFileFormat<QuakeLmpFile>.FromBytes(byte[] data) => QuakeLmpReader.FromBytes(data);
  static QuakeLmpFile IImageFileFormat<QuakeLmpFile>.FromStream(Stream stream) => QuakeLmpReader.FromStream(stream);
  static byte[] IImageFileFormat<QuakeLmpFile>.ToBytes(QuakeLmpFile file) => QuakeLmpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QuakeLmpFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static QuakeLmpFile FromRawImage(RawImage image) {
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
