using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.KofaxKfx;

/// <summary>In-memory representation of a Kofax Group 4 fax image image.</summary>
public sealed class KofaxKfxFile : IImageFileFormat<KofaxKfxFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<KofaxKfxFile>.PrimaryExtension => ".kfx";
  static string[] IImageFileFormat<KofaxKfxFile>.FileExtensions => [".kfx"];
  static FormatCapability IImageFileFormat<KofaxKfxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static KofaxKfxFile IImageFileFormat<KofaxKfxFile>.FromFile(FileInfo file) => KofaxKfxReader.FromFile(file);
  static KofaxKfxFile IImageFileFormat<KofaxKfxFile>.FromBytes(byte[] data) => KofaxKfxReader.FromBytes(data);
  static KofaxKfxFile IImageFileFormat<KofaxKfxFile>.FromStream(Stream stream) => KofaxKfxReader.FromStream(stream);
  static byte[] IImageFileFormat<KofaxKfxFile>.ToBytes(KofaxKfxFile file) => KofaxKfxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(KofaxKfxFile file) {
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

  public static KofaxKfxFile FromRawImage(RawImage image) {
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
