using System;
using FileFormat.Core;

namespace FileFormat.HpGrob;

/// <summary>In-memory representation of a HP-48/49 GROB graphic object image.</summary>
public readonly record struct HpGrobFile : IImageFormatReader<HpGrobFile>, IImageToRawImage<HpGrobFile>, IImageFromRawImage<HpGrobFile>, IImageFormatWriter<HpGrobFile> {

  internal const int HeaderSize = 10;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<HpGrobFile>.PrimaryExtension => ".grob";
  static string[] IImageFormatMetadata<HpGrobFile>.FileExtensions => [".grob", ".hp", ".gro2", ".gro4"];
  static HpGrobFile IImageFormatReader<HpGrobFile>.FromSpan(ReadOnlySpan<byte> data) => HpGrobReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HpGrobFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<HpGrobFile>.ToBytes(HpGrobFile file) => HpGrobWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(HpGrobFile file) {
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
