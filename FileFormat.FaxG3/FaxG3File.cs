using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FaxG3;

/// <summary>In-memory representation of a Raw Group 3 fax image image.</summary>
public sealed class FaxG3File : IImageFileFormat<FaxG3File> {

  internal const int HeaderSize = 6;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<FaxG3File>.PrimaryExtension => ".g3";
  static string[] IImageFileFormat<FaxG3File>.FileExtensions => [".g3"];
  static FormatCapability IImageFileFormat<FaxG3File>.Capabilities => FormatCapability.MonochromeOnly;
  static FaxG3File IImageFileFormat<FaxG3File>.FromFile(FileInfo file) => FaxG3Reader.FromFile(file);
  static FaxG3File IImageFileFormat<FaxG3File>.FromBytes(byte[] data) => FaxG3Reader.FromBytes(data);
  static FaxG3File IImageFileFormat<FaxG3File>.FromStream(Stream stream) => FaxG3Reader.FromStream(stream);
  static byte[] IImageFileFormat<FaxG3File>.ToBytes(FaxG3File file) => FaxG3Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(FaxG3File file) {
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

  public static FaxG3File FromRawImage(RawImage image) {
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
