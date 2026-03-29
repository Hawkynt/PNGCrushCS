using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HpGrob;

/// <summary>In-memory representation of a HP-48/49 GROB graphic object image.</summary>
public sealed class HpGrobFile : IImageFileFormat<HpGrobFile> {

  internal const int HeaderSize = 10;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<HpGrobFile>.PrimaryExtension => ".grob";
  static string[] IImageFileFormat<HpGrobFile>.FileExtensions => [".grob", ".hp", ".gro2", ".gro4"];
  static FormatCapability IImageFileFormat<HpGrobFile>.Capabilities => FormatCapability.MonochromeOnly;
  static HpGrobFile IImageFileFormat<HpGrobFile>.FromFile(FileInfo file) => HpGrobReader.FromFile(file);
  static HpGrobFile IImageFileFormat<HpGrobFile>.FromBytes(byte[] data) => HpGrobReader.FromBytes(data);
  static HpGrobFile IImageFileFormat<HpGrobFile>.FromStream(Stream stream) => HpGrobReader.FromStream(stream);
  static byte[] IImageFileFormat<HpGrobFile>.ToBytes(HpGrobFile file) => HpGrobWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(HpGrobFile file) {
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

  public static HpGrobFile FromRawImage(RawImage image) {
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
