using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PsionPic;

/// <summary>In-memory representation of a Psion Series bitmap image.</summary>
public sealed class PsionPicFile : IImageFileFormat<PsionPicFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<PsionPicFile>.PrimaryExtension => ".ppic";
  static string[] IImageFileFormat<PsionPicFile>.FileExtensions => [".ppic"];
  static FormatCapability IImageFileFormat<PsionPicFile>.Capabilities => FormatCapability.MonochromeOnly;
  static PsionPicFile IImageFileFormat<PsionPicFile>.FromFile(FileInfo file) => PsionPicReader.FromFile(file);
  static PsionPicFile IImageFileFormat<PsionPicFile>.FromBytes(byte[] data) => PsionPicReader.FromBytes(data);
  static PsionPicFile IImageFileFormat<PsionPicFile>.FromStream(Stream stream) => PsionPicReader.FromStream(stream);
  static byte[] IImageFileFormat<PsionPicFile>.ToBytes(PsionPicFile file) => PsionPicWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PsionPicFile file) {
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

  public static PsionPicFile FromRawImage(RawImage image) {
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
